using System;
using System.Collections.Generic;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// WorldRenderer partial: PASS 3 content drawing, status overlay, layer draw callbacks,
/// queue methods for child layers.
/// </summary>
public partial class WorldRenderer
{
    private static readonly List<int> _charSortBuffer = new(16);

    /// <summary>
    /// Draw PASS 3 content: ground objects, characters, layer 3, status overlay.
    /// Called by ContentLayer._Draw().
    /// </summary>
    public void DrawContent(CanvasItem canvas)
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null) return;

        // Opt 5: Pre-compute reusable colors for ground objects and fully-opaque trees.
        // These constants avoid per-tile Color allocations in the common case.
        const float objBright = 220f / 255f;
        const float treeBright = 220f / 255f;
        var objColor = new Color(objBright, objBright, objBright, 1f);
        var treeFullColor = new Color(treeBright, treeBright, treeBright, 1f);

        // Terrain layers (L2 objects, L3 trees) + ground objects — large buffer for big sprites
        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            // Opt 4: use pre-computed screen Y for this row
            float tileScreenY = _screenYCache[y - _frameMinY];
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                // Opt 4: use pre-computed screen X for this column
                Vector2 tilePos = new Vector2(_screenXCache[x - _frameMinX], tileScreenY);
                if (!TryResolveTile(x, y, out var tile)) continue;

                // Ground objects — skip if same GRH exists in L3 (prevents z-fighting flicker)
                if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0
                    && objGrh != tile.Layer3)
                {
                    var light = GetContentLightColor(x, y);
                    DrawTileGrhTo(canvas, objGrh, tilePos, center: true, modulate: MultiplyColor(objColor, light));
                }

                // Characters/NPCs — only within viewport bounds (no large buffer)
                if (x >= _frameCharMinX && x <= _frameCharMaxX && y >= _frameCharMinY && y <= _frameCharMaxY)
                {
                    var charsHere = GetCharsAt(x, y);
                    // Sort by effective Y for correct isometric z-order (higher Y = drawn on top)
                    IEnumerable<int> sortedChars;
                    if (charsHere.Count > 1)
                    {
                        _charSortBuffer.Clear();
                        _charSortBuffer.AddRange(charsHere);
                        _charSortBuffer.Sort((a, b) =>
                        {
                            float ay = _state.Characters.TryGetValue(a, out var ca) ? ca.MoveOffsetY : 0f;
                            float by = _state.Characters.TryGetValue(b, out var cb) ? cb.MoveOffsetY : 0f;
                            return ay.CompareTo(by);
                        });
                        sortedChars = _charSortBuffer;
                    }
                    else
                    {
                        sortedChars = charsHere;
                    }
                    foreach (var cid in sortedChars)
                    {
                        if (!_state.Characters.TryGetValue(cid, out var ch)) continue;
                        if (ch.Invisible && cid != _state.UserCharIndex) continue;

                        float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
                        float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

                        CharRenderer.DrawCharacter((Node2D)canvas, ch, new Vector2(charPx, charPy),
                                                   _data, _animator, _deltaMs, _state, this,
                                                   charTileX: x, charTileY: y, charIdx: cid);
                    }
                }

                // Layer 3 (trees/objects) — dimmed slightly, with proximity transparency for trees
                if (tile.Layer3 > 0)
                {
                    float l3Alpha = 1f;
                    if ((_state.Config?.TreeRoofTransparency ?? true) && IsTree(tile.Layer3))
                    {
                        float smoothUserX = _frameUserX + _state.ScreenOffsetX / 32f;
                        float smoothUserY = _frameUserY + _state.ScreenOffsetY / 32f;
                        float dx = Math.Abs(x - smoothUserX);
                        float dy = Math.Abs(y - smoothUserY);
                        const float innerX = 3f, innerY = 2f;
                        const float outerX = 5f, outerY = 7f;
                        float tx = dx <= innerX ? 0f : dx >= outerX ? 1f : (dx - innerX) / (outerX - innerX);
                        float ty = dy <= innerY ? 0f : dy >= outerY ? 1f : (dy - innerY) / (outerY - innerY);
                        float t = Math.Max(tx, ty);
                        float treeAlpha = (_state.Config?.TreeTransparencyAlpha ?? 47) / 100f;
                        l3Alpha = treeAlpha + (1f - treeAlpha) * t;
                    }
                    Color l3Color = l3Alpha < 1f ? new Color(treeBright, treeBright, treeBright, l3Alpha) : treeFullColor;
                    l3Color = MultiplyColor(l3Color, GetContentLightColor(x, y));
                    DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true, modulate: l3Color);
                }
            }
        }

        // Arrow projectiles — draw after characters/trees so they appear on top
        if (_state.ActiveArrows.Count > 0)
        {
            float originX = _frameUserX * TileSize - ResolutionManager.HalfRenderTilesX * TileSize - _framePixelOffsetX;
            float originY = _frameUserY * TileSize - ResolutionManager.HalfRenderTilesY * TileSize - _framePixelOffsetY;

            for (int i = 0; i < _state.ActiveArrows.Count; i++)
            {
                var arrow = _state.ActiveArrows[i];
                if (!arrow.Active || arrow.GrhIndex <= 0) continue;

                float screenX = arrow.X - originX;
                float screenY = arrow.Y - originY;

                // Rotate arrow to face target
                float dx = arrow.TargetX - arrow.X;
                float dy = arrow.TargetY - arrow.Y;
                float angle = MathF.Atan2(dy, dx);

                // Resolve sprite for centering
                int frame = _animator?.GetCurrentFrame(arrow.GrhIndex, _data) ?? 0;
                var resolved = _data?.ResolveGrh(arrow.GrhIndex, frame);
                if (resolved == null || resolved.FileNum <= 0) continue;

                var texture = _data?.Textures?.GetTexture(resolved.FileNum);
                if (texture == null) continue;

                float hw = resolved.PixelWidth / 2f;
                float hh = resolved.PixelHeight / 2f;

                // Draw rotated around center
                canvas.DrawSetTransform(new Vector2(screenX, screenY), angle);
                var srcRect = new Rect2(resolved.SX, resolved.SY, resolved.PixelWidth, resolved.PixelHeight);
                canvas.DrawTextureRectRegion(texture, new Rect2(-hw, -hh, resolved.PixelWidth, resolved.PixelHeight),
                                              srcRect);
                canvas.DrawSetTransform(Vector2.Zero, 0f);
            }
        }

        // Spell travel beams — cosmetic crackling energy bolt from caster to target.
        if (_state.ActiveBeams.Count > 0)
            DrawSpellBeamsTo(canvas);

        // Status overlay (VB6: drawCounters — paralysis/invisibility bars + status icons)
        DrawStatusOverlayTo(canvas);
    }

    // ── Spell travel beam (crackling energy bolt caster→target) ─────────────
    // Immediate-mode port of the multi-layer glow + electric-jitter beam:
    //   • 3 stacked polylines (wide soft halo → colored mid → white-hot core)
    //   • the polyline is broken into jittered segments so it reads as a
    //     crackling arc, not a ruler-straight laser
    //   • time phases: strike (snap in) → flash (impact overshoot) → hold → fade
    //   • a small muzzle glow disc at the caster origin
    private const float BeamFlashTime = 0.05f;    // fraction: brief impact overshoot on appear
    private const float BeamFadeStart = 0.78f;    // fraction: hold, then fade over the last ~22%
    private const float BeamSegmentLen = 16f;     // px between jitter vertices
    private const float BeamMaxJitter = 4f;       // px perpendicular wobble (subtle, magical)

    private void DrawSpellBeamsTo(CanvasItem canvas)
    {
        float originX = _frameUserX * TileSize - ResolutionManager.HalfRenderTilesX * TileSize - _framePixelOffsetX;
        float originY = _frameUserY * TileSize - ResolutionManager.HalfRenderTilesY * TileSize - _framePixelOffsetY;
        const float anchorY = -22f; // chest height above tile center

        for (int i = 0; i < _state!.ActiveBeams.Count; i++)
        {
            var beam = _state.ActiveBeams[i];
            if (!_state.Characters.TryGetValue(beam.CasterCharIndex, out var caster)) continue;
            if (!_state.Characters.TryGetValue(beam.TargetCharIndex, out var target)) continue;

            Vector2 from = new Vector2(
                caster.PosX * TileSize + 16f + (float)Math.Round(caster.MoveOffsetX) - originX,
                caster.PosY * TileSize + 16f + (float)Math.Round(caster.MoveOffsetY) - originY + anchorY);
            Vector2 to = new Vector2(
                target.PosX * TileSize + 16f + (float)Math.Round(target.MoveOffsetX) - originX,
                target.PosY * TileSize + 16f + (float)Math.Round(target.MoveOffsetY) - originY + anchorY);

            float life = Math.Clamp(beam.ElapsedMs / beam.DurationMs, 0f, 1f);

            // --- Time phases: the bolt strikes instantly (full length at once),
            // flashes briefly, holds for most of its life, then fades out. ---
            float intensity; // overall alpha multiplier
            float widthMul;  // width overshoot on the appear flash
            if (life <= BeamFlashTime)
            {
                float f = life / BeamFlashTime;
                intensity = 1f;
                widthMul = Mathf.Lerp(1.6f, 1f, f); // brief spark on impact, then settle
            }
            else if (life <= BeamFadeStart)
            {
                intensity = 1f; widthMul = 1f;      // hold (~1s)
            }
            else
            {
                intensity = 1f - (life - BeamFadeStart) / (1f - BeamFadeStart);
                widthMul = 1f;                       // fade out
            }
            if (intensity <= 0f) continue;

            // --- Build the jittered polyline (full length: instant impact) ---
            // Re-roll the wobble a few times per second so it shimmers like a live bolt.
            int jitterSeed = beam.JitterSeed + (int)(beam.ElapsedMs / 45f);
            var pts = BuildBeamPolyline(from, to, 1f, jitterSeed);
            if (pts.Length < 2) continue;

            // --- Thin magical bolt: faint aura halo → colored glow → white-hot core ---
            // Very narrow, with a barely-there halo so the bright core reads as
            // the bolt itself and the glow is just a subtle shimmer around it.
            Color halo = new Color(0.75f, 0.85f, 1f, 0.07f * intensity); // faint arcane aura
            Color mid = new Color(0.75f, 0.85f, 1f, 0.4f * intensity);
            Color core = new Color(1f, 1f, 1f, intensity);
            canvas.DrawPolyline(pts, halo, 3f * widthMul, true);
            canvas.DrawPolyline(pts, mid, 1.3f * widthMul, true);
            canvas.DrawPolyline(pts, core, 0.6f * widthMul, true);

            // --- Small muzzle glow at the caster origin ---
            float glow = intensity;
            canvas.DrawCircle(from, 3.5f * widthMul, new Color(0.7f, 0.85f, 1f, 0.1f * glow));
            canvas.DrawCircle(from, 1.8f * widthMul, new Color(0.9f, 0.95f, 1f, 0.35f * glow));
            canvas.DrawCircle(from, 1f, new Color(1f, 1f, 1f, 0.85f * glow));
        }
    }

    // Break the caster→target segment into vertices offset perpendicular to the
    // line by a deterministic (per-seed) random amount, tapered to zero at both
    // ends so the bolt still terminates exactly on caster and target.
    private static Vector2[] BuildBeamPolyline(Vector2 from, Vector2 to, float drawT, int seed)
    {
        Vector2 full = to - from;
        float len = full.Length();
        if (len < 0.001f) return System.Array.Empty<Vector2>();

        Vector2 dir = full / len;
        Vector2 normal = new Vector2(-dir.Y, dir.X);
        float drawnLen = len * Math.Clamp(drawT, 0f, 1f);
        int segments = Math.Max(2, (int)MathF.Ceiling(len / BeamSegmentLen));

        var rng = new System.Random(seed);
        var pts = new System.Collections.Generic.List<Vector2>(segments + 2);
        pts.Add(from);
        for (int i = 1; i < segments; i++)
        {
            float along = len * i / segments;
            if (along > drawnLen) break;
            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * BeamMaxJitter;
            // Taper wobble to zero near both endpoints.
            float edge = Math.Min(along, len - along) / (BeamSegmentLen * 1.5f);
            jitter *= Math.Clamp(edge, 0f, 1f);
            pts.Add(from + dir * along + normal * jitter);
        }
        pts.Add(from + dir * drawnLen);
        return pts.ToArray();
    }

    private Color GetContentLightColor(int x, int y)
    {
        if (_state == null || !(_state.Config?.ShowLights ?? true))
            return Colors.White;
        return LightSystem.GetTileLight(_state, x, y);
    }

    private static Color MultiplyColor(Color color, Color light)
    {
        return new Color(color.R * light.R, color.G * light.G, color.B * light.B, color.A);
    }

    // Pre-computed status overlay colors
    private static readonly Color StatusColorParalyzed = new Color(1f, 0.2f, 0.2f);
    private static readonly Color StatusColorInvisible = new Color(0.6f, 0.6f, 1f);
    private static readonly Color StatusColorMeditating = new Color(0.4f, 0.8f, 1f);
    private static readonly Color StatusColorResting = new Color(0.4f, 1f, 0.4f);
    private static readonly Color StatusColorNavigating = new Color(0.3f, 0.7f, 1f);

    private void DrawStatusOverlayTo(CanvasItem canvas)
    {
        if (_state == null || _data == null) return;

        int slot = 0;

        if (_state.UserParalyzed)
        {
            DrawStatusIconTo(canvas, slot, 23610, _state.ParalysisTimer, _state.ParalysisMaxTimer, "PARALIZADO",
                           StatusColorParalyzed);
            slot++;
        }

        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh) && selfCh.Invisible)
        {
            float inviCurrent = selfCh.InvisibleCountdown;
            bool isSpellInvi = selfCh.InvisibleMaxCountdown > 0;
            string inviLabel = isSpellInvi ? "INVISIBLE" : "OCULTO";
            DrawStatusIconTo(canvas, slot, 23611, inviCurrent, selfCh.InvisibleMaxCountdown, inviLabel,
                           StatusColorInvisible);
            slot++;
        }

        if (_state.Meditating)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "MEDITANDO",
                           StatusColorMeditating);
            slot++;
        }

        if (_state.Resting)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "DESCANSANDO",
                           StatusColorResting);
            slot++;
        }

        if (_state.UserNavigating)
        {
            DrawStatusLabelRight(canvas, "NAVEGANDO", StatusColorNavigating);
        }
    }

    private void DrawStatusIconTo(CanvasItem canvas, int slot, int grhIcon, float current, float max,
                                 string label, Color labelColor)
    {
        float baseX = 10f;
        float baseY = 5f + slot * 38f;

        if (grhIcon > 0 && _data != null)
        {
            CharRenderer.DrawGrh(canvas, _data, grhIcon, 0, new Vector2(baseX, baseY));
        }

        if (max > 0 && current >= 0)
        {
            float barX = baseX + 3;
            float barY = baseY + 35;
            float barW = 25;
            float barH = 6;

            ((Node2D)canvas).DrawRect(new Rect2(barX, barY, barW, barH),
                     new Color(0.49f, 0.49f, 0.49f, 0.59f));
            float fill = Math.Clamp(current / max, 0f, 1f) * barW;
            if (fill > 0)
            {
                ((Node2D)canvas).DrawRect(new Rect2(barX, barY, fill, barH),
                         new Color(1f, 1f, 0f, 0.78f));
            }
        }

        if (grhIcon <= 0 && _data?.Fonts?[1] != null)
        {
            _data.Fonts[1]!.DrawText(canvas, (int)baseX, (int)baseY + 2, label, labelColor);
        }
    }

    /// <summary>
    /// Draw a status label on the top-right corner of the viewport.
    /// </summary>
    private void DrawStatusLabelRight(CanvasItem canvas, string label, Color color)
    {
        if (_data?.Fonts?[1] == null) return;
        float vpW = ResolutionManager.RenderTilesX * 32f;
        float x = vpW - 10f - (label.Length * 8f); // approximate right-align
        _data.Fonts[1]!.DrawText(canvas, (int)x, 7, label, color);
    }

    /// <summary>
    /// Draw pending reflected auras on a given canvas (used by ReflectedAuraLayer).
    /// Uses DrawSetTransform Y-flip (same as character body reflection) so that
    /// tileHeight centering and Offset are handled identically to normal auras.
    /// </summary>
    public void DrawPendingReflAuras(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color, angle, mirrorY) in _pendingReflAuraDraws)
        {
            // Set Y-flip transform around mirrorY (same as DrawReflection for body)
            if (angle != 0f)
            {
                // Rotating: combine rotation + Y-flip
                // We need to draw centered, so resolve sprite dimensions for center calc
                var resolved = _data.ResolveGrh(grhIndex, frame);
                if (resolved == null || resolved.FileNum <= 0) continue;
                var texture = _data.Textures?.GetTexture(resolved.FileNum);
                if (texture == null) continue;

                int sx = resolved.SX, sy = resolved.SY;
                int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
                int texW = texture.GetWidth(), texH = texture.GetHeight();
                if (texW > 0) sx = sx % texW;
                if (texH > 0) sy = sy % texH;
                if (sx + pw > texW) pw = texW - sx;
                if (sy + ph > texH) ph = texH - sy;
                if (pw <= 0 || ph <= 0) continue;

                // Compute draw position with centering (same as DrawGrh center=true)
                float drawX = pos.X;
                float drawY = pos.Y;
                if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
                    drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
                if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
                    drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;

                // Mirror the sprite center, then apply rotation + Y-flip
                float cx = drawX + pw / 2f;
                float cy = drawY + ph / 2f;
                float reflCy = 2f * mirrorY - cy;
                ((Node2D)canvas).DrawSetTransform(new Vector2(cx, reflCy), angle, new Vector2(1f, -1f));
                var srcRect = new Rect2(sx, sy, pw, ph);
                var destRect = new Rect2(-pw / 2f, -ph / 2f, pw, ph);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f);
            }
            else
            {
                // Non-rotating: use global Y-flip transform (same as character body)
                ((Node2D)canvas).DrawSetTransform(new Vector2(0f, mirrorY * 2f), 0f, new Vector2(1f, -1f));
                CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
        }
    }

    /// <summary>
    /// Draw queued character body reflections (+ FX). Called by ReflectionBodyLayer,
    /// which draws AFTER ReflectedAuraLayer so auras are behind the body.
    /// </summary>
    public void DrawReflectionBodies(Node2D canvas)
    {
        if (_data == null || _animator == null) return;

        foreach (var (ch, pos, headOffset, heading) in _pendingReflBodyDraws)
        {
            CharRenderer.DrawReflection(canvas, ch, pos, headOffset, heading, _data, _animator);
            CharRenderer.DrawReflectionFx(canvas, ch, pos, headOffset, heading, _data, _animator);
        }
    }

    /// <summary>
    /// Draw PASS 1b: all non-water L1 tiles. PASS 1 only draws water, so this
    /// is the sole draw for terrain — no double-drawing. Also naturally masks
    /// any reflection overflow onto land.
    /// </summary>
    public void DrawNonWaterMask(CanvasItem canvas)
    {
        if (_state?.MapData == null || _data == null || _animator == null) return;

        // Must cover same range as L2/L3 terrain buffer to prevent flash at edges
        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            float sy = _screenYCache[y - _frameMinY];
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                if (!TryResolveTile(x, y, out var tile)) continue;
                if (tile.Layer1 <= 0) continue;
                if (IsWaterGrh(tile.Layer1)) continue; // skip water

                // Opt 4: use pre-computed screen coords
                Vector2 pos = new Vector2(_screenXCache[x - _frameMinX], sy);
                DrawTileGrhTo(canvas, tile.Layer1, pos, center: false);
            }
        }
    }

    /// <summary>
    /// Draw PASS 2: Layer 2 tiles with per-tile light modulate.
    /// Called by Layer2Layer._Draw().
    /// </summary>
    public void DrawLayer2(CanvasItem canvas)
    {
        if (_state?.MapData == null || _data == null || _animator == null) return;

        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            float sy = _screenYCache[y - _frameMinY];
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                if (!TryResolveTile(x, y, out var tile)) continue;
                if (tile.Layer2 <= 0) continue;

                // Opt 4: use pre-computed screen coords
                Vector2 pos = new Vector2(_screenXCache[x - _frameMinX], sy);
                DrawTileGrhTo(canvas, tile.Layer2, pos, center: true);
            }
        }
    }

    /// <summary>
    /// Draw pending normal aura draws on a given canvas (used by AuraAdditiveLayer).
    /// Only normal (non-reflected) auras — reflected auras are handled by ReflectedAuraLayer.
    /// </summary>
    public void DrawPendingAuras(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color, angle) in _pendingAuraDraws)
        {
            if (angle != 0f)
            {
                // Rotating aura — use DrawSetTransform for rotation around sprite center
                var resolved = _data.ResolveGrh(grhIndex, frame);
                if (resolved == null || resolved.FileNum <= 0) continue;
                var texture = _data.Textures?.GetTexture(resolved.FileNum);
                if (texture == null) continue;

                int sx = resolved.SX, sy = resolved.SY;
                int pw = resolved.PixelWidth, ph = resolved.PixelHeight;
                int texW = texture.GetWidth(), texH = texture.GetHeight();
                if (texW > 0) sx = sx % texW;
                if (texH > 0) sy = sy % texH;
                if (sx + pw > texW) pw = texW - sx;
                if (sy + ph > texH) ph = texH - sy;
                if (pw <= 0 || ph <= 0) continue;

                // Center the GRH
                float drawX = pos.X;
                float drawY = pos.Y;
                if (resolved.TileWidth != 1f && resolved.TileWidth > 0)
                    drawX -= (int)(resolved.TileWidth * (TileSize / 2)) - TileSize / 2;
                if (resolved.TileHeight != 1f && resolved.TileHeight > 0)
                    drawY -= (int)(resolved.TileHeight * TileSize) - TileSize;

                float cx = drawX + pw / 2f;
                float cy = drawY + ph / 2f;
                ((Node2D)canvas).DrawSetTransform(new Vector2(cx, cy), angle);
                var srcRect = new Rect2(sx, sy, pw, ph);
                var destRect = new Rect2(-pw / 2f, -ph / 2f, pw, ph);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f);
            }
            else
            {
                CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
            }
        }
    }

    /// <summary>
    /// Draw all pending particle draws on a given canvas (used by AdditiveParticleLayer).
    /// </summary>
    public void DrawPendingParticles(CanvasItem canvas)
    {
        if (_data == null) return;

        foreach (var (grhIndex, frame, pos, color) in _pendingMapParticleDraws)
        {
            CharRenderer.DrawEffectGrh(canvas, _data, grhIndex, frame, pos, color);
        }
        foreach (var (grhIndex, frame, pos, color) in _pendingCharParticleDraws)
        {
            CharRenderer.DrawEffectGrh(canvas, _data, grhIndex, frame, pos, color);
        }
    }

    /// <summary>
    /// Queue a character-attached particle draw for the additive blend layer.
    /// Called by CharRenderer.DrawCharParticles.
    /// </summary>
    public void QueueCharParticleDraw(int grhIndex, int frame, Vector2 pos, Color color)
    {
        _pendingCharParticleDraws.Add((grhIndex, frame, pos, color));
    }

    /// <summary>
    /// Queue an aura draw for the aura additive layer.
    /// Called by CharRenderer.CollectAuraDraws.
    /// </summary>
    public void QueueAuraDraw(int grhIndex, int frame, Vector2 pos, Color color, float angle)
    {
        _pendingAuraDraws.Add((grhIndex, frame, pos, color, angle));
    }

    /// <summary>
    /// Queue a reflected aura draw. Position is the NORMAL aura position (same as regular aura).
    /// The renderer mirrors it using DrawSetTransform, same as character body reflection.
    /// </summary>
    public void QueueReflAuraDraw(int grhIndex, int frame, Vector2 pos, Color color, float angle, float mirrorY)
    {
        _pendingReflAuraDraws.Add((grhIndex, frame, pos, color, angle, mirrorY));
    }

    /// <summary>
    /// Queue a dialog draw for the overlay layer (above all characters).
    /// Called by CharRenderer.DrawDialog.
    /// </summary>
    public void QueueDialogDraw(string[] lines, int textCenterX, int baseY, int fontSize, Color color)
    {
        _pendingDialogDraws.Add((lines, textCenterX, baseY, fontSize, color));
    }

    /// <summary>
    /// Draw pending dialog text on a given canvas (used by DialogOverlayLayer).
    /// </summary>
    public void DrawPendingDialogs(CanvasItem canvas)
    {
        if (_data?.Fonts?[1] == null) return;
        var font = _data.Fonts[1]!;

        foreach (var (lines, textCenterX, baseY, fontSize, color) in _pendingDialogDraws)
        {
            int offset = -(fontSize + 2) * (lines.Length - 1);
            for (int i = 0; i < lines.Length; i++)
            {
                int lineY = baseY + offset + 2;
                font.DrawText(canvas, textCenterX, lineY, lines[i], color, center: true);
                offset += fontSize + 5;
            }
        }
    }

    /// <summary>
    /// Draw all pending roof tiles on a given canvas (used by RoofLayer).
    /// </summary>
    public void DrawPendingRoof(CanvasItem canvas)
    {
        if (_data == null || _animator == null) return;
        foreach (var (grhIndex, pos, modulate) in _pendingRoofDraws)
        {
            int frame = _animator.GetCurrentFrame(grhIndex, _data);
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, modulate);
        }
    }
}
