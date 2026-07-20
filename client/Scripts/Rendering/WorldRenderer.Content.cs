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
    private readonly List<int> _charSortBuffer = new(16);

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
                canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
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
    private const float BeamAppearTime = 0.08f;   // fraction: quick fade-in so it never pops harshly
    private const float BeamImpactStart = 0.68f;  // fraction: travel reaches target here
    private const float BeamTrailFraction = 0.34f;
    private const int BeamTrailSegments = 7;
    private const float BeamMaxCurve = 7f;

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
            float appear = Smooth01(life / BeamAppearTime);
            float fade = life < BeamImpactStart
                ? 1f
                : 1f - Smooth01((life - BeamImpactStart) / (1f - BeamImpactStart));
            float intensity = appear * fade;
            if (intensity <= 0f) continue;

            // --- Build the jittered polyline (full length: instant impact) ---
            // Continuous organic noise (same value-noise the extended particle motor
            // uses for Turbulence) instead of a stepped random re-seed — the bolt
            // flows and drifts smoothly rather than flickering in discrete jumps.
            Vector2 full = to - from;
            float len = full.Length();
            if (len < 1f) continue;

            float travelRaw = Math.Clamp(life / BeamImpactStart, 0f, 1f);
            float travel = EaseOutCubic(travelRaw);
            float trailStart = Math.Max(0f, travel - BeamTrailFraction);
            float curveSeed = ParticleNoise.Sample(beam.JitterSeed * 0.017f, 3.11f, 0f) * 2f - 1f;
            float curveOffset = curveSeed * Math.Min(BeamMaxCurve, len * 0.055f);

            // --- Thin magical bolt: faint aura halo → colored glow → white-hot core ---
            // Very narrow, with a barely-there halo so the bright core reads as
            // the bolt itself and the glow is just a subtle shimmer around it.
            if (travel > 0.01f)
            {
                for (int s = 0; s < BeamTrailSegments; s++)
                {
                    float u0 = s / (float)BeamTrailSegments;
                    float u1 = (s + 1) / (float)BeamTrailSegments;
                    float t0 = Mathf.Lerp(trailStart, travel, u0);
                    float t1 = Mathf.Lerp(trailStart, travel, u1);
                    Vector2 p0 = SampleBeamArc(from, to, t0, curveOffset);
                    Vector2 p1 = SampleBeamArc(from, to, t1, curveOffset);

                    float headWeight = MathF.Pow(u1, 1.65f);
                    float segmentAlpha = intensity * (0.12f + 0.88f * headWeight);
                    float widthPulse = 0.96f + 0.04f * MathF.Sin(beam.ElapsedMs * 0.035f + s);
                    DrawBeamSegment(canvas, p0, p1, segmentAlpha, widthPulse);
                }

                Vector2 head = SampleBeamArc(from, to, travel, curveOffset);
                float headAlpha = intensity * (0.75f + 0.25f * (1f - travelRaw));
                canvas.DrawCircle(head, 5.0f, new Color(0.25f, 0.55f, 1f, 0.10f * headAlpha));
                canvas.DrawCircle(head, 2.2f, new Color(0.75f, 0.92f, 1f, 0.44f * headAlpha));
                canvas.DrawCircle(head, 0.9f, new Color(1f, 0.97f, 0.78f, 0.90f * headAlpha));
            }

            // --- Small muzzle glow at the caster origin ---
            if (life >= BeamImpactStart)
            {
                float impactT = Math.Clamp((life - BeamImpactStart) / (1f - BeamImpactStart), 0f, 1f);
                float impactAlpha = 1f - Smooth01(impactT);
                float radius = 2.2f + 7.5f * Smooth01(impactT);
                canvas.DrawCircle(to, radius, new Color(0.35f, 0.68f, 1f, 0.055f * impactAlpha));
                canvas.DrawCircle(to, 2.5f + 1.5f * impactT, new Color(0.75f, 0.9f, 1f, 0.16f * impactAlpha));
                canvas.DrawCircle(to, 1.2f, new Color(1f, 0.96f, 0.78f, 0.65f * impactAlpha));
            }

            // --- Energy motes drifting along the bolt ---
            // Purely cosmetic, generated fresh every frame from (beam id, index,
            // time) — no persistent particle stream, so it never touches
            // ParticleSystem/state.MapParticles or the wire protocol. Queued onto
            // the same additive layer real particles use, so they glow like the
            // rest of the particle system instead of flat-alpha blending.
            QueueBeamMotes(beam, from, to, trailStart, travel, curveOffset, intensity);
        }
    }

    private static float Smooth01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutCubic(float t)
    {
        t = 1f - Math.Clamp(t, 0f, 1f);
        return 1f - t * t * t;
    }

    private static Vector2 SampleBeamArc(Vector2 from, Vector2 to, float t, float curveOffset)
    {
        t = Math.Clamp(t, 0f, 1f);
        Vector2 full = to - from;
        float len = full.Length();
        if (len < 0.001f) return from;
        Vector2 normal = new Vector2(-full.Y / len, full.X / len);
        return from.Lerp(to, t) + normal * (MathF.Sin(t * MathF.PI) * curveOffset);
    }

    private static void DrawBeamSegment(CanvasItem canvas, Vector2 p0, Vector2 p1, float alpha, float widthMul)
    {
        if (alpha <= 0f) return;
        canvas.DrawLine(p0, p1, new Color(0.20f, 0.48f, 1f, 0.055f * alpha), 5.2f * widthMul, true);
        canvas.DrawLine(p0, p1, new Color(0.45f, 0.74f, 1f, 0.18f * alpha), 2.2f * widthMul, true);
        canvas.DrawLine(p0, p1, new Color(1f, 0.96f, 0.78f, 0.68f * alpha), 0.85f * widthMul, true);
    }

    private const int BeamMoteCount = 3;
    private const int BeamMoteGrh = 27452; // same spark sprite as Particles.ini def [1] "Fountain"

    /// <summary>
    /// Scatter a handful of glowing motes along the caster→target segment, each
    /// drifting off the line via the same coherent noise the beam wobble and
    /// Turbulence-enabled particles use, so they read as sparks of the same energy
    /// rather than a separate effect layered on top.
    /// </summary>
    private void QueueBeamMotes(
        SpellBeam beam,
        Vector2 from,
        Vector2 to,
        float trailStart,
        float trailEnd,
        float curveOffset,
        float intensity)
    {
        if (_data == null || _animator == null) return;
        Vector2 full = to - from;
        float len = full.Length();
        if (len < 1f) return;
        if (trailEnd <= trailStart + 0.001f) return;
        Vector2 normal = new Vector2(-full.Y / len, full.X / len);

        int frame = _animator.GetCurrentFrame(BeamMoteGrh, _data);
        float time = beam.ElapsedMs * 0.004f;

        for (int m = 0; m < BeamMoteCount; m++)
        {
            float phase = beam.JitterSeed * 0.031f + m * 3.7f;
            float u = (m + 0.5f) / BeamMoteCount;
            float arcT = Mathf.Lerp(trailStart, trailEnd, u);

            float drift = (ParticleNoise.Sample(arcT * 8f, phase, time) * 2f - 1f) * 3.2f;
            Vector2 pos = SampleBeamArc(from, to, arcT, curveOffset) + normal * drift;

            float twinkle = 0.5f + 0.5f * MathF.Sin(time * 7f + m * 1.9f);
            float alpha = intensity * (0.08f + 0.24f * twinkle) * (0.35f + 0.65f * u);
            var color = new Color(0.85f, 0.92f, 1f, alpha);

            _pendingMapParticleDraws.Add((BeamMoteGrh, frame, pos, color, 0f, 0.34f));
        }
    }

    // Break the caster→target segment into vertices offset perpendicular to the
    // line by continuous coherent noise (ParticleNoise — same helper the extended
    // particle motor's Turbulence uses), tapered to zero at both ends so the bolt
    // still terminates exactly on caster and target. Sampling along (position,
    // noisePhase) instead of re-seeding System.Random per-segment/per-tick makes
    // adjacent vertices — and adjacent frames — flow into each other smoothly,
    // reading as a living current instead of a flickering zig-zag.
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
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
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
                ((Node2D)canvas).DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
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

        foreach (var (grhIndex, frame, pos, color, angle, scale) in _pendingMapParticleDraws)
        {
            CharRenderer.DrawEffectGrh(canvas, _data, grhIndex, frame, pos, color, angle, scale);
        }
        foreach (var (grhIndex, frame, pos, color, angle, scale) in _pendingCharParticleDraws)
        {
            CharRenderer.DrawEffectGrh(canvas, _data, grhIndex, frame, pos, color, angle, scale);
        }
    }

    /// <summary>
    /// Queue a character-attached particle draw for the additive blend layer.
    /// Called by CharRenderer.DrawCharParticles. angle/scale default to the
    /// classic no-op values (0f/1f) for callers that don't pass them.
    /// </summary>
    public void QueueCharParticleDraw(int grhIndex, int frame, Vector2 pos, Color color, float angle = 0f, float scale = 1f)
    {
        _pendingCharParticleDraws.Add((grhIndex, frame, pos, color, angle, scale));
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
