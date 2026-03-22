using Godot;
using System;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws a soft red gradient on the innermost tile of each safe-zone edge,
/// warning the player that the zone ends there.
///
/// Approach: four gradient quads (left / right / top / bottom), one tile wide,
/// using per-vertex alpha so the GPU interpolates a smooth linear gradient across
/// the tile — no per-tile DrawRect, no stepping artifacts, no jitter.
///
/// Camera anchor: mirrors WorldRenderer exactly —
///   camera_center = UserPosX - AddToUserPosX  (old tile during scroll)
///   pixel_offset  = -Round(ScreenOffsetX)
/// This ensures the fog stays glued to world coordinates, not player position.
/// </summary>
public partial class SafeZoneBorderLayer : Node2D
{
    private readonly GameState _state;

    // Soft red at the very edge, transparent one tile in
    private const float MaxAlpha = 0.20f;
    private const int   TileSize = 32;

    private static readonly Color EdgeColor  = new(0.80f, 0.08f, 0.08f, MaxAlpha);
    private static readonly Color ClearColor = new(0.80f, 0.08f, 0.08f, 0f);

    // Last known safe zone bounds — persists after leaving the zone
    private int _safeX1, _safeY1, _safeX2, _safeY2;
    private bool _hasSafeZone;

    // Zone warning text state machine
    private bool _wasInsideSafe;     // previous frame state
    private bool _initialized;
    private string _warningText = "";
    private float _warningAlpha;     // current alpha (0..1)
    private float _warningTarget;    // target alpha for smooth transitions
    private float _warningTimer;     // countdown for timed messages
    private enum WarningState { None, NearBorder, JustLeft, JustEntered }
    private WarningState _warnState = WarningState.None;

    public SafeZoneBorderLayer(GameState state)
    {
        _state = state;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Update warning state
        switch (_warnState)
        {
            case WarningState.JustLeft:
                // "Estás en zona insegura" — fade out over 3s
                _warningTimer -= dt;
                _warningAlpha = Math.Clamp(_warningTimer / 3f, 0f, 1f);
                if (_warningTimer <= 0) _warnState = WarningState.None;
                break;
            case WarningState.JustEntered:
                // "Estás en zona segura" — show 2s then transition to NearBorder
                _warningTimer -= dt;
                if (_warningTimer <= 0.5f)
                    _warningAlpha = Math.Clamp(_warningTimer / 0.5f, 0f, 1f); // fade last 0.5s
                if (_warningTimer <= 0) _warnState = WarningState.NearBorder;
                break;
            case WarningState.NearBorder:
                // Smooth transition toward target alpha (set in DrawZoneWarning)
                _warningAlpha += (_warningTarget - _warningAlpha) * Math.Min(1f, dt * 4f);
                break;
            case WarningState.None:
                _warningAlpha += (0f - _warningAlpha) * Math.Min(1f, dt * 4f);
                break;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        // Remember last safe zone so we keep drawing it after leaving
        if (_state.CurrentZoneSafe && _state.CurrentZoneX1 > 0 && _state.CurrentZoneX2 > 0)
        {
            _safeX1 = _state.CurrentZoneX1;
            _safeY1 = _state.CurrentZoneY1;
            _safeX2 = _state.CurrentZoneX2;
            _safeY2 = _state.CurrentZoneY2;
            _hasSafeZone = true;
        }

        if (!_hasSafeZone) return;

        int zX1 = _safeX1;
        int zY1 = _safeY1;
        int zX2 = _safeX2;
        int zY2 = _safeY2;

        // Mirror WorldRenderer camera formula exactly:
        //   _frameUserX = UserPosX - AddToUserPosX  (renders at old tile during scroll)
        //   _framePixelOffsetX = Round(-ScreenOffsetX)
        int uX  = _state.UserPosX - _state.AddToUserPosX;
        int uY  = _state.UserPosY - _state.AddToUserPosY;
        int hX  = ResolutionManager.HalfTilesX;
        int hY  = ResolutionManager.HalfTilesY;
        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;

        float offX = (float)Math.Round(-_state.ScreenOffsetX);
        float offY = (float)Math.Round(-_state.ScreenOffsetY);

        // Screen pixel of left edge of tile t:   (t - uX + hX) * TS + offX
        // Screen pixel of top  edge of tile t:   (t - uY + hY) * TS + offY
        float LeftOf(int t)   => (t - uX + hX) * TileSize + offX;
        float RightOf(int t)  => (t - uX + hX + 1) * TileSize + offX;
        float TopOf(int t)    => (t - uY + hY) * TileSize + offY;
        float BottomOf(int t) => (t - uY + hY + 1) * TileSize + offY;

        // Zone boundary in screen pixels — the exact edge of the zone
        float sL = LeftOf(zX1);   // left  edge of zone
        float sR = RightOf(zX2);  // right edge of zone
        float sT = TopOf(zY1);    // top   edge of zone
        float sB = BottomOf(zY2); // bottom edge of zone

        // Fully off-screen → skip
        if (sR <= 0 || sL >= vpW || sB <= 0 || sT >= vpH) return;

        // Fog straddles the boundary: half tile inside + half tile outside.
        // Peak (max red) is at the zone edge, fading to clear in both directions.
        float half = TileSize * 0.5f;

        float yTop = Math.Max(0, sT - half);
        float yBot = Math.Min(vpH, sB + half);

        // ── Left border ──
        {
            float peak = sL;           // zone boundary
            float inner = peak + half; // half tile inside zone
            float outer = peak - half; // half tile outside zone
            // Inside half: clear → peak
            if (inner > 0 && peak < vpW)
                DrawGradH(peak, inner, yTop, yBot, EdgeColor, ClearColor);
            // Outside half: peak → clear
            if (peak > 0 && outer < vpW)
                DrawGradH(outer, peak, yTop, yBot, ClearColor, EdgeColor);
        }

        // ── Right border ──
        {
            float peak = sR;
            float inner = peak - half;
            float outer = peak + half;
            if (inner > 0 && peak < vpW)
                DrawGradH(inner, peak, yTop, yBot, ClearColor, EdgeColor);
            if (peak > 0 && outer < vpW)
                DrawGradH(peak, outer, yTop, yBot, EdgeColor, ClearColor);
        }

        // For top/bottom, use x range that excludes corner overlaps
        float iL = Math.Max(0, sL + half);
        float iR = Math.Min(vpW, sR - half);

        // ── Top border ──
        if (iR > iL)
        {
            float peak = sT;
            float inner = peak + half;
            float outer = peak - half;
            if (inner > 0 && peak < vpH)
                DrawGradV(iL, iR, peak, inner, EdgeColor, ClearColor);
            if (peak > 0 && outer < vpH)
                DrawGradV(iL, iR, outer, peak, ClearColor, EdgeColor);
        }

        // ── Bottom border ──
        if (iR > iL)
        {
            float peak = sB;
            float inner = peak - half;
            float outer = peak + half;
            if (inner > 0 && peak < vpH)
                DrawGradV(iL, iR, inner, peak, ClearColor, EdgeColor);
            if (peak > 0 && outer < vpH)
                DrawGradV(iL, iR, peak, outer, EdgeColor, ClearColor);
        }

        // ── Proximity warning text ──
        DrawZoneWarning(vpW, zX1, zY1, zX2, zY2);
    }

    private void DrawZoneWarning(int vpW, int zX1, int zY1, int zX2, int zY2)
    {
        const int ProximityTiles = 4;
        int px = _state.UserPosX, py = _state.UserPosY;

        bool insideSafe = px >= zX1 && px <= zX2 && py >= zY1 && py <= zY2;

        // Detect transitions
        if (_initialized)
        {
            if (_wasInsideSafe && !insideSafe)
            {
                _warnState = WarningState.JustLeft;
                _warningText = "Saliste de la zona segura";
                _warningAlpha = 1f;
                _warningTimer = 3f;
            }
            else if (!_wasInsideSafe && insideSafe)
            {
                _warnState = WarningState.JustEntered;
                _warningText = "Entraste a zona segura";
                _warningAlpha = 1f;
                _warningTimer = 2f;
            }
        }
        _wasInsideSafe = insideSafe;
        _initialized = true;

        // If inside safe zone near border and no timed message active → show proximity warning
        if (insideSafe && _warnState != WarningState.JustEntered)
        {
            int minDist = Math.Min(
                Math.Min(px - zX1, zX2 - px),
                Math.Min(py - zY1, zY2 - py));

            if (minDist <= ProximityTiles)
            {
                _warnState = WarningState.NearBorder;
                _warningText = "Estás por salir de la zona segura";
                _warningTarget = 1f; // smooth fade handled in _Process
            }
            else if (_warnState == WarningState.NearBorder)
            {
                _warningTarget = 0f; // fade out smoothly
                if (_warningAlpha < 0.01f) _warnState = WarningState.None;
            }
        }

        // Draw text
        if (_warningAlpha < 0.01f || _warningText.Length == 0) return;

        var baseColor = _warnState == WarningState.JustLeft
            ? new Color(1f, 0.4f, 0.3f)     // red — unsafe
            : _warnState == WarningState.JustEntered
                ? new Color(0.3f, 1f, 0.5f)  // green — safe
                : new Color(1f, 0.85f, 0.3f); // yellow — warning near border

        var font = ThemeDB.FallbackFont;
        int fontSize = 14;
        var textSize = font.GetStringSize(_warningText, HorizontalAlignment.Center, -1, fontSize);
        float tx = (vpW - textSize.X) * 0.5f;
        float ty = 28f;
        float a = _warningAlpha * 0.9f;

        DrawString(font, new Vector2(tx + 1, ty + 1), _warningText, HorizontalAlignment.Left,
            -1, fontSize, new Color(0, 0, 0, a * 0.5f));
        DrawString(font, new Vector2(tx, ty), _warningText, HorizontalAlignment.Left,
            -1, fontSize, new Color(baseColor.R, baseColor.G, baseColor.B, a));
    }

    // Horizontal gradient: x0→x1 fades from colorLeft to colorRight, uniform y
    private void DrawGradH(float x0, float x1, float y0, float y1,
                           Color colorLeft, Color colorRight)
    {
        if (x1 <= x0 || y1 <= y0) return;
        DrawPolygon(
            new[] {
                new Vector2(x0, y0), new Vector2(x1, y0),
                new Vector2(x1, y1), new Vector2(x0, y1)
            },
            new[] { colorLeft, colorRight, colorRight, colorLeft }
        );
    }

    // Vertical gradient: y0→y1 fades from colorTop to colorBottom, uniform x
    private void DrawGradV(float x0, float x1, float y0, float y1,
                           Color colorTop, Color colorBot)
    {
        if (x1 <= x0 || y1 <= y0) return;
        DrawPolygon(
            new[] {
                new Vector2(x0, y0), new Vector2(x1, y0),
                new Vector2(x1, y1), new Vector2(x0, y1)
            },
            new[] { colorTop, colorTop, colorBot, colorBot }
        );
    }
}
