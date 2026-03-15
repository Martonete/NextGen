using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.Rendering;

/// <summary>
/// Draws a gradient fog overlay on the extended viewport area (tiles beyond the core 17x13).
/// Only active when the resolution is larger than 800x600.
/// Creates a smooth darkness gradient at the viewport edges so the extra visible area
/// fades naturally into darkness rather than having a hard cut.
/// </summary>
public partial class FogOverlayLayer : Node2D
{
    // Fog darkness at the very edge of the viewport (0=transparent, 1=opaque black)
    private const float MaxFogAlpha = 0.65f;

    public override void _Ready()
    {
        // Only need to draw once (resolution requires scene reload to change)
        QueueRedraw();
    }

    public override void _Draw()
    {
        int extraX = ResolutionManager.ExtraTilesX;
        int extraY = ResolutionManager.ExtraTilesY;
        if (extraX <= 0 && extraY <= 0) return;

        int vpW = ResolutionManager.ViewportW;
        int vpH = ResolutionManager.ViewportH;
        int coreW = 17 * 32; // 544
        int coreH = 13 * 32; // 416

        // Core area starts at the center of the viewport
        int coreLeft = (vpW - coreW) / 2;
        int coreTop = (vpH - coreH) / 2;
        int coreRight = coreLeft + coreW;
        int coreBottom = coreTop + coreH;

        // Draw fog strips along each edge with gradient
        int tileSize = 32;

        // Left fog (columns from 0 to coreLeft)
        if (coreLeft > 0)
        {
            for (int x = 0; x < coreLeft; x += tileSize)
            {
                float dist = (float)(coreLeft - x) / coreLeft;
                float alpha = dist * MaxFogAlpha;
                int w = System.Math.Min(tileSize, coreLeft - x);
                DrawRect(new Rect2(x, 0, w, vpH), new Color(0, 0, 0, alpha));
            }
        }

        // Right fog (columns from coreRight to vpW)
        if (coreRight < vpW)
        {
            int fogW = vpW - coreRight;
            for (int x = coreRight; x < vpW; x += tileSize)
            {
                float dist = (float)(x - coreRight + tileSize) / fogW;
                float alpha = dist * MaxFogAlpha;
                int w = System.Math.Min(tileSize, vpW - x);
                DrawRect(new Rect2(x, 0, w, vpH), new Color(0, 0, 0, alpha));
            }
        }

        // Top fog (rows from 0 to coreTop, only in the core X range to avoid double-fog corners)
        if (coreTop > 0)
        {
            for (int y = 0; y < coreTop; y += tileSize)
            {
                float dist = (float)(coreTop - y) / coreTop;
                float alpha = dist * MaxFogAlpha;
                int h = System.Math.Min(tileSize, coreTop - y);
                DrawRect(new Rect2(coreLeft, y, coreW, h), new Color(0, 0, 0, alpha));
            }
        }

        // Bottom fog (rows from coreBottom to vpH, only in core X range)
        if (coreBottom < vpH)
        {
            int fogH = vpH - coreBottom;
            for (int y = coreBottom; y < vpH; y += tileSize)
            {
                float dist = (float)(y - coreBottom + tileSize) / fogH;
                float alpha = dist * MaxFogAlpha;
                int h = System.Math.Min(tileSize, vpH - y);
                DrawRect(new Rect2(coreLeft, y, coreW, h), new Color(0, 0, 0, alpha));
            }
        }
    }
}
