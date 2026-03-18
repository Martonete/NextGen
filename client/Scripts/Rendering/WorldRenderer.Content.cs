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
    /// <summary>
    /// Draw PASS 3 content: ground objects, characters, layer 3, status overlay.
    /// Called by ContentLayer._Draw().
    /// </summary>
    public void DrawContent(CanvasItem canvas)
    {
        if (_state == null || _data == null || _animator == null) return;
        if (_state.MapData == null) return;

        // Terrain layers (L2 objects, L3 trees) + ground objects — large buffer for big sprites
        for (int y = _frameMinY; y <= _frameMaxY; y++)
        {
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                Vector2 tilePos = TileToScreen(x, y, _frameUserX, _frameUserY,
                                                _framePixelOffsetX, _framePixelOffsetY);
                ref var tile = ref _state.MapData.Tiles[x, y];

                // DEBUG: ground objects completely disabled to test flicker
                // if (_state.GroundObjects.TryGetValue((x, y), out int objGrh) && objGrh > 0)
                // {
                //     DrawTileGrhTo(canvas, objGrh, tilePos, center: true);
                // }

                // Characters/NPCs — only within viewport bounds (no large buffer)
                if (x >= _frameCharMinX && x <= _frameCharMaxX && y >= _frameCharMinY && y <= _frameCharMaxY)
                {
                    var charsHere = GetCharsAt(x, y);
                    for (int ci = 0; ci < charsHere.Count; ci++)
                    {
                        if (!_state.Characters.TryGetValue(charsHere[ci], out var ch)) continue;
                        if (ch.Invisible && charsHere[ci] != _state.UserCharIndex) continue;

                        float charPx = tilePos.X + (float)Math.Round(ch.MoveOffsetX);
                        float charPy = tilePos.Y + (float)Math.Round(ch.MoveOffsetY);

                        CharRenderer.DrawCharacter((Node2D)canvas, ch, new Vector2(charPx, charPy),
                                                   _data, _animator, _deltaMs, _state, this,
                                                   charTileX: x, charTileY: y);
                    }
                }

                // Layer 3 (trees/objects)
                if (tile.Layer3 > 0)
                {
                    float l3Alpha = 1f;
                    if ((_state.Config?.TreeRoofTransparency ?? true) && IsTree(tile.Layer3))
                    {
                        // Tile-based distance only (no sub-pixel ScreenOffset) to prevent flicker
                        float dx = Math.Abs(x - _frameUserX);
                        float dy = Math.Abs(y - _frameUserY);
                        const float innerX = 3f, innerY = 2f;
                        const float outerX = 5f, outerY = 7f;
                        float tx = dx <= innerX ? 0f : dx >= outerX ? 1f : (dx - innerX) / (outerX - innerX);
                        float ty = dy <= innerY ? 0f : dy >= outerY ? 1f : (dy - innerY) / (outerY - innerY);
                        float t = Math.Max(tx, ty);
                        float treeAlpha = (_state.Config?.TreeTransparencyAlpha ?? 47) / 100f;
                        l3Alpha = treeAlpha + (1f - treeAlpha) * t;
                    }
                    DrawTileGrhTo(canvas, tile.Layer3, tilePos, center: true,
                        modulate: l3Alpha < 0.99f ? new Color(1f, 1f, 1f, l3Alpha) : (Color?)null);
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

        // Status overlay (VB6: drawCounters — paralysis/invisibility bars + status icons)
        DrawStatusOverlayTo(canvas);
    }

    private void DrawStatusOverlayTo(CanvasItem canvas)
    {
        if (_state == null || _data == null) return;

        if (_state.UserParalyzed && _state.ParalysisTimer > 0)
        {
            // Decrement in real seconds (deltaMs is in milliseconds)
            _state.ParalysisTimer -= _deltaMs / 1000f;
            if (_state.ParalysisTimer < 0) _state.ParalysisTimer = 0;
        }

        int slot = 0;

        if (_state.UserParalyzed)
        {
            DrawStatusIconTo(canvas, slot, 23610, _state.ParalysisTimer, _state.ParalysisMaxTimer, "PARALIZADO",
                           new Color(1f, 0.2f, 0.2f));
            slot++;
        }

        if (_state.Characters.TryGetValue(_state.UserCharIndex, out var selfCh) && selfCh.Invisible)
        {
            // Decrement invisibility countdown in real seconds
            if (selfCh.InvisibleCountdown > 0)
            {
                selfCh.InvisibleCountdownTimer += _deltaMs;
                if (selfCh.InvisibleCountdownTimer >= 1000f)
                {
                    selfCh.InvisibleCountdownTimer -= 1000f;
                    selfCh.InvisibleCountdown--;
                }
            }
            float inviCurrent = selfCh.InvisibleCountdown;
            // Spell invisibility has countdown (label "INVISIBLE"), hide skill has no countdown (label "OCULTO")
            bool isSpellInvi = selfCh.InvisibleMaxCountdown > 0;
            string inviLabel = isSpellInvi ? "INVISIBLE" : "OCULTO";
            DrawStatusIconTo(canvas, slot, 23611, inviCurrent, selfCh.InvisibleMaxCountdown, inviLabel,
                           new Color(0.6f, 0.6f, 1f));
            slot++;
        }

        if (_state.Meditating)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "MEDITANDO",
                           new Color(0.4f, 0.8f, 1f));
            slot++;
        }

        if (_state.Resting)
        {
            DrawStatusIconTo(canvas, slot, 0, -1, -1, "DESCANSANDO",
                           new Color(0.4f, 1f, 0.4f));
            slot++;
        }

        if (_state.UserNavigating)
        {
            // Draw on the right side to avoid overlapping countdown bars
            DrawStatusLabelRight(canvas, "NAVEGANDO", new Color(0.3f, 0.7f, 1f));
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
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer1 <= 0) continue;
                if (IsWaterGrh(tile.Layer1)) continue; // skip water

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY,
                                            _framePixelOffsetX, _framePixelOffsetY);
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
            for (int x = _frameMinX; x <= _frameMaxX; x++)
            {
                ref var tile = ref _state.MapData.Tiles[x, y];
                if (tile.Layer2 <= 0) continue;

                Vector2 pos = TileToScreen(x, y, _frameUserX, _frameUserY, _framePixelOffsetX, _framePixelOffsetY);
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
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
        }
        foreach (var (grhIndex, frame, pos, color) in _pendingCharParticleDraws)
        {
            CharRenderer.DrawGrh(canvas, _data, grhIndex, frame, pos, true, color);
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
