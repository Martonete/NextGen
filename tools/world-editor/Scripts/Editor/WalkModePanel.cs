#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Walk mode: simulates the in-game AO view with selectable resolution.
/// Renders a centered character with walk animation, roof/tree transparency,
/// and blocked tile collision. Shows NPCs, objects, exits on the map.
/// Supports map transitions via exit tiles.
/// </summary>
public partial class WalkModePanel : Control
{
    // ── Dependencies ────────────────────────────────────────────────────────
    public MapData? Map;
    public GrhData[]? Grhs;
    public TextureManager? Textures;
    public BodyAnimData[]? Bodies;
    public HeadAnimData[]? Heads;

    // NPC/Object rendering data (same arrays as MapViewport)
    public int[]? ObjGrhs;
    public int[]? NpcBodies;
    public int[]? NpcHeads;
    public int[]? NpcBodyGrhs;
    public int[]? NpcHeadOfsX;
    public int[]? NpcHeadOfsY;
    public int[]? HeadGrhs;

    // Door data for interactive doors
    public Dictionary<int, DoorInfo>? DoorData; // obj_index → DoorInfo
    // Track toggled doors: maps (mapNum, x, y) → toggled ObjIndex (so we don't persist changes)
    private readonly Dictionary<(int map, int x, int y), int> _toggledDoors = new();

    // Particle engine (shared with MapViewport for live particle rendering in walk mode)
    public ParticleEngine? Particles;

    // Zone data for ambient override and weather in walk mode
    public MapZoneData? Zones;

    // Map loading — for exit tile transitions
    public string MapDir = "";  // directory where Mapa{N}.map/.inf/.dat live

    // ── Resolution presets ───────────────────────────────────────────────────
    public static readonly (string Label, int W, int H)[] Resolutions =
    {
        ("800x600 (AO Clásico)", 800, 600),
        ("1024x768",             1024, 768),
        ("1152x864",             1152, 864),
        ("1280x720 (HD)",        1280, 720),
        ("1280x960",             1280, 960),
        ("1366x768",             1366, 768),
        ("1600x900",             1600, 900),
        ("1920x1080 (Full HD)",  1920, 1080),
    };

    // ── Viewport metrics (recalculated on resolution change) ───────────────
    private const int TileSize = 32;
    private const int HalfTileSize = TileSize / 2; // 16
    private int _halfTilesX = 12; // default for 800x600
    private int _halfTilesY = 9;
    private int _viewTilesX = 25;
    private int _viewTilesY = 19;
    private int _viewWidth = 800;
    private int _viewHeight = 608;
    private const int ExtraTiles = 3;
    private const int ExtraTilesLarge = 12;
    private int _extraTilesX, _extraTilesY;
    private ImageTexture? _fogTexture;
    public int ViewWidth => _viewWidth;
    public int ViewHeight => _viewHeight;

    /// <summary>Force CPU lighting recalculation (call after editing lights/zones in the main editor).</summary>
    public void InvalidateLighting() => _cpuLightsDirty = true;

    // Movement
    private const float PixelsPerSecond = 200f; // VB6 exact: ScrollPixels=8 per 40ms tick = 200px/s
    private const float ScrollPixels = 8f;

    // ── Character state ─────────────────────────────────────────────────────
    public int CharX = 50, CharY = 50; // current tile position (1-indexed)
    public int BodyIndex = 1;
    public int HeadIndex = 1;

    private int _heading = 3; // 1=N 2=E 3=S 4=W
    private bool _isMoving;
    private float _moveOffsetX, _moveOffsetY; // pixel offset during smooth scroll
    private double _globalTime; // ms, for tile animations

    // ── Input state ─────────────────────────────────────────────────────────
    private bool _keyUp, _keyDown, _keyLeft, _keyRight;

    // ── Roof fade (smooth alpha, matching client WorldRenderer.UpdateRoofFade) ──
    private float _roofAlpha = 255f; // 0=fully transparent, 255=fully opaque
    private const float RoofFadeRate = 8f; // alpha change per frame
    private bool _diagPrinted;

    private int _lastMapNumber = -1;

    // ── CPU per-tile lighting (per-tile modulate, doesn't affect overlays/UI) ──
    private readonly WalkModeLightSystem _cpuLights = new();
    private bool _cpuLightsDirty = true;

    // ── Weather FX ──
    private readonly WeatherFx _weather = new();

    // ── Particle overlay (additive-blend child CanvasItem for correct particle glow) ──
    private WalkParticleOverlay? _particleOverlay;
    internal float _particleDrawOfsX, _particleDrawOfsY; // set in _Draw, read by overlay

    public override void _Ready()
    {
        SetResolution(800, 600);
        ClipContents = true;
        FocusMode = FocusModeEnum.All;
        GrabFocus();

        // Additive particle layer — separate CanvasItem so particles glow correctly
        var particleOverlay = new WalkParticleOverlay { Owner = this, Name = "ParticleOverlay" };
        particleOverlay.Material = MapViewport.LoadParticleShader();
        particleOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        particleOverlay.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(particleOverlay);
        _particleOverlay = particleOverlay;
    }

    /// <summary>Recalculates viewport metrics for the given resolution (client-faithful port of ResolutionManager.ApplyResolution).</summary>
    public void SetResolution(int windowW, int windowH)
    {
        float uiScale = windowH / 600f;
        int leftMargin = (int)(13 * uiScale);
        int topMargin = (int)(149 * uiScale);
        int sidebarW = (int)(240 * uiScale);
        int bottomBar = (int)(35 * uiScale);
        int sidebarGap = (int)(3 * uiScale);

        int availW = windowW - sidebarW - leftMargin - sidebarGap;
        int availH = windowH - topMargin - bottomBar;
        int tilesX = availW / TileSize;
        int tilesY = availH / TileSize;
        if (tilesX % 2 == 0) tilesX--;
        if (tilesY % 2 == 0) tilesY--;

        // Margin-shrink expansion (same as client)
        int leftoverH = availH - tilesY * TileSize;
        int extraNeededH = 2 * TileSize - leftoverH;
        int minTopMargin = (int)(128 * uiScale);
        if (extraNeededH > 0 && topMargin - extraNeededH >= minTopMargin)
            tilesY += 2;

        int leftoverW = availW - tilesX * TileSize;
        int extraNeededW = 2 * TileSize - leftoverW;
        int minLeftMargin = Math.Max(4, (int)(4 * uiScale));
        if (extraNeededW > 0 && leftMargin - extraNeededW >= minLeftMargin)
            tilesX += 2;

        tilesX = Math.Max(17, tilesX);
        tilesY = Math.Max(13, tilesY);

        _halfTilesX = tilesX / 2;
        _halfTilesY = tilesY / 2;
        _viewTilesX = tilesX;
        _viewTilesY = tilesY;
        _viewWidth = tilesX * TileSize;
        _viewHeight = tilesY * TileSize;
        _extraTilesX = (tilesX - 17) / 2;
        _extraTilesY = (tilesY - 13) / 2;

        CustomMinimumSize = new Vector2(_viewWidth, _viewHeight);
        Size = new Vector2(_viewWidth, _viewHeight);

        BuildFogTexture();
        _cpuLightsDirty = true;
        QueueRedraw();
    }

    private void BuildFogTexture()
    {
        if (_extraTilesX <= 0 && _extraTilesY <= 0)
        {
            _fogTexture = null;
            return;
        }

        const float MaxAlpha = 0.12f;
        const float TransitionPx = 32f;

        int texW = _viewWidth / 4;
        int texH = _viewHeight / 4;
        int coreW = 544 / 4;  // 17*32 / 4
        int coreH = 416 / 4;  // 13*32 / 4
        float centerX = texW / 2f;
        float centerY = texH / 2f;
        float halfCoreW = coreW / 2f;
        float halfCoreH = coreH / 2f;
        float transition = TransitionPx / 4f;

        var img = Image.CreateEmpty(texW, texH, false, Image.Format.Rgba8);

        for (int py = 0; py < texH; py++)
            for (int px = 0; px < texW; px++)
            {
                float dx = Math.Max(0, Math.Abs(px - centerX) - halfCoreW);
                float dy = Math.Max(0, Math.Abs(py - centerY) - halfCoreH);
                float dist = Math.Max(dx, dy);

                float alpha;
                if (dist <= 0f)
                    alpha = 0f;
                else if (dist < transition)
                {
                    float t = dist / transition;
                    t = t * t * (3f - 2f * t);
                    alpha = t * MaxAlpha;
                }
                else
                    alpha = MaxAlpha;

                img.SetPixel(px, py, new Color(0, 0, 0, alpha));
            }

        _fogTexture = ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        _globalTime += delta * 1000.0;

        // Step the shared particle simulation so streams animate in walk mode too
        Particles?.Update((float)delta);

        // Update weather from current zone
        var currentZone = Zones?.GetZoneAt(CharX, CharY);
        _weather.Lluvia = currentZone?.Lluvia ?? false;
        _weather.Nieve  = currentZone?.Nieve  ?? false;
        _weather.Niebla = currentZone?.Niebla ?? false;
        _weather.Update((float)delta, Size);

        if (_isMoving)
        {
            float advance = PixelsPerSecond * (float)delta;

            if (_moveOffsetX != 0)
            {
                float sign = Math.Sign(_moveOffsetX);
                _moveOffsetX -= sign * advance;
                if (Math.Sign(_moveOffsetX) != sign)
                    _moveOffsetX = 0;
            }
            if (_moveOffsetY != 0)
            {
                float sign = Math.Sign(_moveOffsetY);
                _moveOffsetY -= sign * advance;
                if (Math.Sign(_moveOffsetY) != sign)
                    _moveOffsetY = 0;
            }

            if (_moveOffsetX == 0 && _moveOffsetY == 0)
            {
                _isMoving = false;


                // Check for map exit AFTER arriving at new tile
                CheckExitTile();

                TryMoveFromInput();
            }
        }
        else
        {
            TryMoveFromInput();
        }

        // Smooth roof fade (matching client WorldRenderer.UpdateRoofFade)
        bool underRoof = IsUnderRoof(CharX, CharY);
        if (underRoof)
        {
            _roofAlpha -= RoofFadeRate;
            if (_roofAlpha < 0) _roofAlpha = 0;
        }
        else
        {
            _roofAlpha += RoofFadeRate;
            if (_roofAlpha > 255) _roofAlpha = 255;
        }

        QueueRedraw();
    }

    private void TryMoveFromInput()
    {
        if (_isMoving) return;
        if (_keyUp) TryMove(0, -1, 1);
        else if (_keyDown) TryMove(0, 1, 3);
        else if (_keyLeft) TryMove(-1, 0, 4);
        else if (_keyRight) TryMove(1, 0, 2);
    }

    private void TryMove(int dx, int dy, int heading)
    {
        _heading = heading;
        if (Map == null) return;

        int nx = CharX + dx;
        int ny = CharY + dy;

        if (!Map.InBounds(nx, ny)) return;
        if (Map.Tiles[nx, ny].Blocked) return;

        CharX = nx;
        CharY = ny;
        _isMoving = true;
        _moveOffsetX = dx * TileSize;
        _moveOffsetY = dy * TileSize;
    }

    // ── Map exit / transition ────────────────────────────────────────────────

    private void CheckExitTile()
    {
        if (Map == null || MapDir.Length == 0) return;
        if (!Map.InBounds(CharX, CharY)) return;

        ref var tile = ref Map.Tiles[CharX, CharY];
        if (tile.ExitMap <= 0) return;

        int destMap = tile.ExitMap;
        int destX = tile.ExitX;
        int destY = tile.ExitY;

        // Check destination map files exist before loading
        string mapFile = Path.Combine(MapDir, $"Mapa{destMap}.map");
        if (!File.Exists(mapFile))
        {
            GD.Print($"[WalkMode] Exit to map {destMap} — file not found: {mapFile}");
            return;
        }

        GD.Print($"[WalkMode] Warp: Mapa{Map.MapNumber} ({CharX},{CharY}) → Mapa{destMap} ({destX},{destY})");

        // Load the new map
        var newMap = MapLoader.Load(MapDir, destMap);

        // Validate destination coordinates
        if (!newMap.InBounds(destX, destY))
        {
            GD.Print($"[WalkMode] Invalid destination ({destX},{destY}) for map {destMap}");
            return;
        }

        // Switch to new map
        Map = newMap;
        CharX = destX;
        CharY = destY;
        _isMoving = false;
        _moveOffsetX = 0;
        _moveOffsetY = 0;

        _diagPrinted = false; // print diagnostics for new map

        // Reload zone data for new map
        if (MapDir.Length > 0)
            Zones = MapZoneData.Load(MapDir, destMap);

        // Rebuild particles and lights for new map
        Particles?.BuildStreamsFromMap(newMap);
        _cpuLightsDirty = true;

        // Update window title
        var parentWindow = GetParent<Window>();
        if (parentWindow != null)
            parentWindow.Title = $"Modo Caminata — Mapa {destMap}: {newMap.Name}";
    }

    private bool IsUnderRoof(int cx, int cy)
    {
        if (Map == null || !Map.InBounds(cx, cy)) return false;
        short trigger = Map.Tiles[cx, cy].Trigger;
        return trigger == 1 || trigger == 2 || trigger == 4;
    }

    // ── Input handling ──────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventKey key)
        {
            bool pressed = key.Pressed;
            bool handled = true;
            switch (key.Keycode)
            {
                case Key.W: case Key.Up: _keyUp = pressed; break;
                case Key.S: case Key.Down: _keyDown = pressed; break;
                case Key.A: case Key.Left: _keyLeft = pressed; break;
                case Key.D: case Key.Right: _keyRight = pressed; break;
                case Key.Escape:
                    if (pressed) GetParent<Window>()?.Hide();
                    break;
                default: handled = false; break;
            }
            if (handled) AcceptEvent();
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.DoubleClick && Map != null)
            {
                // Double-click: try to toggle a door
                float worldX = mb.Position.X - _moveOffsetX;
                float worldY = mb.Position.Y - _moveOffsetY;
                int tx = (int)Math.Floor(worldX / TileSize) - _halfTilesX + CharX;
                int ty = (int)Math.Floor(worldY / TileSize) - _halfTilesY + CharY;
                TryToggleDoor(tx, ty);
                AcceptEvent();
                return;
            }
            if (mb.ShiftPressed && Map != null)
            {
                float worldX = mb.Position.X - _moveOffsetX;
                float worldY = mb.Position.Y - _moveOffsetY;
                int tx = (int)Math.Floor(worldX / TileSize) - _halfTilesX + CharX;
                int ty = (int)Math.Floor(worldY / TileSize) - _halfTilesY + CharY;
                if (Map.InBounds(tx, ty))
                {
                    CharX = tx;
                    CharY = ty;
                    _isMoving = false;
                    _moveOffsetX = 0;
                    _moveOffsetY = 0;
                    CheckExitTile();
                }
            }
            AcceptEvent();
        }
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (Map == null || Grhs == null || Textures == null) return;

        if (!_diagPrinted)
        {
            _diagPrinted = true;
            GD.Print($"[WalkMode] DIAG: MapDir=\"{MapDir}\" Map#={Map.MapNumber} Grhs={Grhs.Length}");
            GD.Print($"[WalkMode] DIAG: ObjGrhs={(ObjGrhs != null ? ObjGrhs.Length.ToString() : "NULL")} NpcBodies={(NpcBodies != null ? NpcBodies.Length.ToString() : "NULL")} NpcBodyGrhs={(NpcBodyGrhs != null ? NpcBodyGrhs.Length.ToString() : "NULL")} HeadGrhs={(HeadGrhs != null ? HeadGrhs.Length.ToString() : "NULL")}");
            GD.Print($"[WalkMode] DIAG: DoorData={(DoorData != null ? DoorData.Count + " doors" : "NULL")}");
            // Count tiles with NPCs, objects, exits
            int npcCount = 0, objCount = 0, exitCount = 0;
            for (int y = 1; y <= 100; y++)
                for (int x = 1; x <= 100; x++)
                {
                    if (Map.InBounds(x, y))
                    {
                        if (Map.Tiles[x, y].NpcIndex > 0) npcCount++;
                        if (Map.Tiles[x, y].ObjIndex > 0) objCount++;
                        if (Map.Tiles[x, y].ExitMap > 0) exitCount++;
                    }
                }
            GD.Print($"[WalkMode] DIAG: Map has {npcCount} NPC tiles, {objCount} Object tiles, {exitCount} Exit tiles");
            if (npcCount > 0 && NpcBodies != null)
            {
                // Print first NPC for debugging
                for (int y = 1; y <= 100; y++)
                    for (int x = 1; x <= 100; x++)
                        if (Map.InBounds(x, y) && Map.Tiles[x, y].NpcIndex > 0)
                        {
                            int ni = Map.Tiles[x, y].NpcIndex;
                            int bi = ni < NpcBodies.Length ? NpcBodies[ni] : -1;
                            int bg = (bi > 0 && NpcBodyGrhs != null && bi < NpcBodyGrhs.Length) ? NpcBodyGrhs[bi] : -1;
                            GD.Print($"[WalkMode] DIAG: First NPC at ({x},{y}) npcIdx={ni} bodyIdx={bi} bodyGrh={bg}");
                            goto doneDiag;
                        }
                doneDiag:;
            }
            if (exitCount > 0)
            {
                for (int y = 1; y <= 100; y++)
                    for (int x = 1; x <= 100; x++)
                        if (Map.InBounds(x, y) && Map.Tiles[x, y].ExitMap > 0)
                        {
                            GD.Print($"[WalkMode] DIAG: First exit at ({x},{y}) → Map{Map.Tiles[x,y].ExitMap} ({Map.Tiles[x,y].ExitX},{Map.Tiles[x,y].ExitY})");
                            goto doneExitDiag;
                        }
                doneExitDiag:;
            }
        }

        DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);

        // Detect map change → rebuild lights
        if (Map.MapNumber != _lastMapNumber)
        {
            _lastMapNumber = Map.MapNumber;
            _cpuLightsDirty = true;
        }

        // CPU per-tile lighting (always works, independent of Godot GPU pipeline)
        if (_cpuLightsDirty)
        {
            _cpuLights.Recalculate(Map, Zones);
            _cpuLightsDirty = false;
        }

        float ofsX = (float)Math.Round(_moveOffsetX);
        float ofsY = (float)Math.Round(_moveOffsetY);

        // L1 uses smaller buffer; L2/L3/L4 need large buffer for multi-tile GRHs
        int minDY_L1 = -_halfTilesY - ExtraTiles;
        int maxDY_L1 = _halfTilesY + ExtraTiles;
        int minDX_L1 = -_halfTilesX - ExtraTiles;
        int maxDX_L1 = _halfTilesX + ExtraTiles;

        int minDY = -_halfTilesY - ExtraTilesLarge;
        int maxDY = _halfTilesY + ExtraTilesLarge;
        int minDX = -_halfTilesX - ExtraTilesLarge;
        int maxDX = _halfTilesX + ExtraTilesLarge;

        // ── Pass 1: Ground (L1) ── top-left aligned, with per-tile lighting
        for (int dy = minDY_L1; dy <= maxDY_L1; dy++)
            for (int dx = minDX_L1; dx <= maxDX_L1; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                float sx = (dx + _halfTilesX) * TileSize + ofsX;
                float sy = (dy + _halfTilesY) * TileSize + ofsY;
                DrawGrh(Map.Tiles[tx, ty].Layer1, sx, sy, _cpuLights.GetTileLight(tx, ty));
            }

        // ── Pass 2: Mask/Alpha (L2) ── centered, large buffer
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                int l2 = Map.Tiles[tx, ty].Layer2;
                if (l2 <= 0) continue;
                float sx = (dx + _halfTilesX) * TileSize + ofsX;
                float sy = (dy + _halfTilesY) * TileSize + ofsY;
                DrawGrhCentered(l2, sx, sy, _cpuLights.GetTileLight(tx, ty));
            }

        // ── Pass 3: Objects + NPCs + L3 + Character — Y-sorted ──
        for (int dy = minDY; dy <= maxDY; dy++)
        {
            int ty = CharY + dy;

            // Draw character at its Y row with per-tile lighting
            if (dy == 0)
            {
                Color charLight = _cpuLights.GetTileLight(CharX, CharY);
                DrawCharacter(ofsX, ofsY, charLight);
            }

            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx;
                if (!Map.InBounds(tx, ty)) continue;

                float sx = (dx + _halfTilesX) * TileSize + ofsX;
                float sy = (dy + _halfTilesY) * TileSize + ofsY;
                Color tileLight = _cpuLights.GetTileLight(tx, ty);

                // Ground objects from .inf data (includes doors)
                DrawTileObject(tx, ty, sx, sy, tileLight);

                // NPCs from .inf data
                DrawTileNpc(tx, ty, sx, sy, tileLight);

                // L3 graphic layer
                int l3 = Map.Tiles[tx, ty].Layer3;
                if (l3 > 0)
                {
                    bool onCharTile = (tx == CharX && ty == CharY);
                    Color mod = onCharTile ? new Color(tileLight.R, tileLight.G, tileLight.B, 0.5f) : tileLight;
                    DrawGrhCentered(l3, sx, sy, mod);
                }
            }
        }

        // ── Pass 4: Roof (L4) — centered, large buffer, smooth alpha fade + lighting
        if (_roofAlpha > 0)
        {
            float roofA = _roofAlpha / 255f;
            for (int dy = minDY; dy <= maxDY; dy++)
                for (int dx = minDX; dx <= maxDX; dx++)
                {
                    int tx = CharX + dx, ty = CharY + dy;
                    if (!Map.InBounds(tx, ty)) continue;
                    int l4 = Map.Tiles[tx, ty].Layer4;
                    if (l4 <= 0) continue;
                    float sx = (dx + _halfTilesX) * TileSize + ofsX;
                    float sy = (dy + _halfTilesY) * TileSize + ofsY;
                    Color rl = _cpuLights.GetTileLight(tx, ty);
                    DrawGrhCentered(l4, sx, sy, new Color(rl.R, rl.G, rl.B, roofA));
                }
        }

        // ── Fog overlay (dark vignette on extra tiles beyond core 17×13) ──
        if (_fogTexture != null && (_extraTilesX > 0 || _extraTilesY > 0))
            DrawTextureRect(_fogTexture, new Rect2(0, 0, _viewWidth, _viewHeight), false);

        // ── Exit markers ──
        DrawExitMarkers(minDX_L1, maxDX_L1, minDY_L1, maxDY_L1, ofsX, ofsY);

        // ── Particles (rain, fire, fountain, etc.) — drawn on additive overlay ──
        _particleDrawOfsX = ofsX;
        _particleDrawOfsY = ofsY;
        _particleOverlay?.QueueRedraw();

        // ── Weather FX ──
        _weather.Draw(this, Size);

        // ── HUD ──
        var font = ThemeDB.Singleton.FallbackFont;
        string mapName = Map.Name.Length > 0 ? Map.Name : $"Mapa {Map.MapNumber}";
        string info = $"{mapName}  ({Map.MapNumber},{CharX},{CharY})  [{HeadingName(_heading)}]";
        DrawString(font, new Vector2(6, Size.Y - 6), info,
            HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.7f));
        DrawString(font, new Vector2(6, 16),
            "WASD/Flechas: caminar  |  Shift+Click: teleport  |  DblClick: puerta  |  Esc: cerrar",
            HorizontalAlignment.Left, -1, 11, new Color(1, 1, 0.8f, 0.5f));
    }

    // ── NPC / Object / Exit rendering ───────────────────────────────────────

    private void DrawTileObject(int tx, int ty, float sx, float sy, Color light)
    {
        if (ObjGrhs == null) return;
        int objIdx = Map!.Tiles[tx, ty].ObjIndex;
        if (objIdx <= 0 || objIdx >= ObjGrhs.Length) return;
        int objGrh = ObjGrhs[objIdx];
        if (objGrh <= 0) return;
        DrawGrhCentered(objGrh, sx, sy, light);
    }

    private void DrawTileNpc(int tx, int ty, float sx, float sy, Color light)
    {
        if (NpcBodies == null || NpcBodyGrhs == null || Grhs == null) return;
        int npcIdx = Map!.Tiles[tx, ty].NpcIndex;
        if (npcIdx <= 0 || npcIdx >= NpcBodies.Length) return;

        int bodyIdx = NpcBodies[npcIdx];
        if (bodyIdx <= 0 || bodyIdx >= NpcBodyGrhs.Length) return;

        int bodyGrh = NpcBodyGrhs[bodyIdx];
        if (bodyGrh > 0)
            DrawAnimGrhCentered(bodyGrh, 0, sx, sy, light);

        if (NpcHeads == null || HeadGrhs == null) return;
        if (npcIdx >= NpcHeads.Length) return;
        int headIdx = NpcHeads[npcIdx];
        if (headIdx <= 0 || headIdx >= HeadGrhs.Length) return;
        int headGrh = HeadGrhs[headIdx];
        if (headGrh <= 0) return;

        int hofX = (NpcHeadOfsX != null && bodyIdx < NpcHeadOfsX.Length) ? NpcHeadOfsX[bodyIdx] : 0;
        int hofY = (NpcHeadOfsY != null && bodyIdx < NpcHeadOfsY.Length) ? NpcHeadOfsY[bodyIdx] : 0;
        DrawAnimGrhCentered(headGrh, 0, sx + hofX, sy + hofY, light);
    }

    private void DrawExitMarkers(int minDX, int maxDX, int minDY, int maxDY, float ofsX, float ofsY)
    {
        if (Map == null) return;
        for (int dy = minDY; dy <= maxDY; dy++)
            for (int dx = minDX; dx <= maxDX; dx++)
            {
                int tx = CharX + dx, ty = CharY + dy;
                if (!Map.InBounds(tx, ty)) continue;
                if (!Map.Tiles[tx, ty].HasExit) continue;

                float sx = (dx + _halfTilesX) * TileSize + ofsX;
                float sy = (dy + _halfTilesY) * TileSize + ofsY;

                DrawRect(new Rect2(sx + 2, sy + 2, TileSize - 4, TileSize - 4),
                    new Color(0.2f, 1f, 0.2f, 0.25f));
                DrawRect(new Rect2(sx + 2, sy + 2, TileSize - 4, TileSize - 4),
                    new Color(0.2f, 1f, 0.2f, 0.5f), false, 1f);
            }
    }

    private void DrawCharacter(float ofsX, float ofsY, Color light = default)
    {
        if (light == default) light = Colors.White;
        if (Bodies == null || Heads == null) return;
        if (BodyIndex <= 0 || BodyIndex >= Bodies.Length) return;

        var body = Bodies[BodyIndex];
        int bodyGrh = body.Walk[_heading];
        if (bodyGrh <= 0) return;

        // Character is ALWAYS at viewport center
        float cx = _halfTilesX * TileSize;
        float cy = _halfTilesY * TileSize;

        // VB6-style walk animation: frame synced to pixel displacement, one frame per 8px step
        int frameCount = GetGrhFrameCount(bodyGrh);
        int bodyFrame = 0;
        if (_isMoving && frameCount > 1)
        {
            // How many pixels we've traveled so far (TileSize - remaining offset)
            float pixelsTraveled = TileSize - Math.Abs(_moveOffsetX) - Math.Abs(_moveOffsetY);
            int steps = (int)(pixelsTraveled / ScrollPixels);
            bodyFrame = steps % frameCount;
        }
        DrawAnimGrhCentered(bodyGrh, bodyFrame, cx, cy, light);

        // Head
        if (HeadIndex > 0 && HeadIndex < Heads.Length)
        {
            int headGrh = Heads[HeadIndex].Head[_heading];
            if (headGrh > 0)
            {
                float hx = cx + body.HeadOffsetX;
                float hy = cy + body.HeadOffsetY;
                DrawAnimGrhCentered(headGrh, 0, hx, hy, light);
            }
        }
    }

    // ── Door toggle ─────────────────────────────────────────────────────────

    private void TryToggleDoor(int tx, int ty)
    {
        if (Map == null) { GD.Print("[WalkMode] TryToggleDoor: Map is null"); return; }
        if (DoorData == null) { GD.Print("[WalkMode] TryToggleDoor: DoorData is null"); return; }
        if (ObjGrhs == null) { GD.Print("[WalkMode] TryToggleDoor: ObjGrhs is null"); return; }
        if (!Map.InBounds(tx, ty)) { GD.Print($"[WalkMode] TryToggleDoor: ({tx},{ty}) out of bounds"); return; }

        // VB6: distance check ≤ 2 tiles
        if (Math.Abs(tx - CharX) > 2 || Math.Abs(ty - CharY) > 2)
        {
            GD.Print($"[WalkMode] TryToggleDoor: ({tx},{ty}) too far from char ({CharX},{CharY})");
            return;
        }

        int tileObj = Map.Tiles[tx, ty].ObjIndex;
        GD.Print($"[WalkMode] TryToggleDoor at ({tx},{ty}): tileObjIdx={tileObj}, DoorData has {DoorData.Count} entries");

        // Find door object on this tile or adjacent tiles (VB6 checks X, X+1, X+1/Y+1, X/Y+1)
        int doorX = tx, doorY = ty;
        var door = FindDoorAt(tx, ty);
        if (door == null)
        {
            // Check adjacent tiles for multi-tile doors
            int[][] offsets = { new[]{1,0}, new[]{-1,0}, new[]{1,1}, new[]{0,1}, new[]{-1,1}, new[]{0,-1} };
            foreach (var off in offsets)
            {
                int ax = tx + off[0], ay = ty + off[1];
                if (!Map.InBounds(ax, ay)) continue;
                var adjDoor = FindDoorAt(ax, ay);
                if (adjDoor != null) { door = adjDoor; doorX = ax; doorY = ay; break; }
            }
        }
        if (door == null)
        {
            GD.Print($"[WalkMode] No door found at ({tx},{ty}) or adjacent tiles");
            return;
        }
        GD.Print($"[WalkMode] Found door: IndexAbierta={door.IndexAbierta} IndexCerrada={door.IndexCerrada} Abierta={door.Abierta} Llave={door.Llave}");

        // VB6: Llave=1 means locked — can't toggle
        if (door.Llave == 1)
        {
            GD.Print($"[WalkMode] Door at ({doorX},{doorY}) is locked");
            return;
        }

        // VB6 logic: Abierta=1 means closed (inverted naming!), Abierta=0 means open
        // If current obj matches IndexCerrada or the door's Abierta=1, it's closed → open it
        int curObjIdx = Map.Tiles[doorX, doorY].ObjIndex;
        bool isClosed = (curObjIdx == door.IndexCerrada) || (door.Abierta == 1 && curObjIdx != door.IndexAbierta);

        int newObjIdx = isClosed ? door.IndexAbierta : door.IndexCerrada;
        if (newObjIdx <= 0) return;

        // Update tile object index
        Map.Tiles[doorX, doorY].ObjIndex = (short)newObjIdx;
        _toggledDoors[(Map.MapNumber, doorX, doorY)] = newObjIdx;

        // VB6: closed → block tiles, open → unblock. Affects (X, Y) and (X-1, Y)
        bool closing = !isClosed;
        Map.Tiles[doorX, doorY].Blocked = closing;
        if (Map.InBounds(doorX - 1, doorY))
            Map.Tiles[doorX - 1, doorY].Blocked = closing;

        // Ensure the alternate object index is also registered in DoorData for toggle-back
        if (!DoorData.ContainsKey(newObjIdx))
        {
            DoorData[newObjIdx] = new DoorInfo
            {
                ObjType = door.ObjType,
                IndexAbierta = door.IndexAbierta,
                IndexCerrada = door.IndexCerrada,
                IndexCerradaLlave = door.IndexCerradaLlave,
                PuertaDoble = door.PuertaDoble,
                Porton = door.Porton,
                Abierta = isClosed ? 0 : 1, // opposite state
                Llave = door.Llave,
                GrhIndex = ObjGrhs.Length > newObjIdx ? ObjGrhs[newObjIdx] : 0,
            };
        }

        GD.Print($"[WalkMode] Door toggle at ({doorX},{doorY}): obj {curObjIdx} -> {newObjIdx} (closing={closing})");

        // Doors changed Blocked tiles → recalculate lighting
        _cpuLightsDirty = true;
    }

    private DoorInfo? FindDoorAt(int x, int y)
    {
        if (Map == null || DoorData == null) return null;
        if (!Map.InBounds(x, y)) return null;
        int objIdx = Map.Tiles[x, y].ObjIndex;
        if (objIdx <= 0) return null;

        // Direct match: this object IS a door
        if (DoorData.TryGetValue(objIdx, out var door)) return door;

        // Indirect match: another door references this objIdx as its open/closed variant
        foreach (var kv in DoorData)
        {
            if (kv.Value.IndexAbierta == objIdx || kv.Value.IndexCerrada == objIdx || kv.Value.IndexCerradaLlave == objIdx)
                return kv.Value;
        }
        return null;
    }

    // ── GRH drawing helpers ─────────────────────────────────────────────────

    private void DrawGrh(int grhIndex, float x, float y, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        // Animate L1 tiles (water, lava, etc.) using _globalTime
        var baseGrh = Grhs[grhIndex];
        int frameIdx = 0;
        if (baseGrh.NumFrames > 1 && baseGrh.Speed > 0)
            frameIdx = (int)(_globalTime * baseGrh.NumFrames / baseGrh.Speed) % baseGrh.NumFrames;

        var grh = ResolveFrame(grhIndex, frameIdx);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2(x, y, grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    private void DrawGrhCentered(int grhIndex, float tileX, float tileY, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var baseGrh = Grhs[grhIndex];
        int frameIdx = 0;
        if (baseGrh.NumFrames > 1 && baseGrh.Speed > 0)
            frameIdx = (int)(_globalTime * baseGrh.NumFrames / baseGrh.Speed) % baseGrh.NumFrames;

        var grh = ResolveFrame(grhIndex, frameIdx);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        // AO centering: multi-tile sprites offset left and up
        float drawX = tileX;
        float drawY = tileY;

        if (grh.TileWidth != 1f && grh.TileWidth > 0)
            drawX -= (int)(grh.TileWidth * HalfTileSize) - HalfTileSize;
        if (grh.TileHeight != 1f && grh.TileHeight > 0)
            drawY -= (int)(grh.TileHeight * TileSize) - TileSize;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    private void DrawAnimGrhCentered(int grhIndex, int frame, float tileX, float tileY, Color mod)
    {
        if (grhIndex <= 0 || Grhs == null || Textures == null) return;
        if (grhIndex >= Grhs.Length) return;

        var grh = ResolveFrame(grhIndex, frame);
        if (grh.FileNum <= 0 || grh.PixelWidth <= 0) return;

        var texture = Textures.GetTexture(grh.FileNum);
        if (texture == null) return;

        float drawX = tileX;
        float drawY = tileY;

        if (grh.TileWidth != 1f && grh.TileWidth > 0)
            drawX -= (int)(grh.TileWidth * HalfTileSize) - HalfTileSize;
        if (grh.TileHeight != 1f && grh.TileHeight > 0)
            drawY -= (int)(grh.TileHeight * TileSize) - TileSize;

        var src = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
        var dst = new Rect2((float)Math.Round(drawX), (float)Math.Round(drawY), grh.PixelWidth, grh.PixelHeight);
        DrawTextureRectRegion(texture, dst, src, mod);
    }

    /// <summary>Draw all active particles from the shared ParticleEngine in walk-mode space.
    /// Walk mode draws tile (tx, ty) at panel pixel ((tx - CharX + _halfTilesX) * TS + ofs).
    /// We use the same transform for particles so they line up.
    /// Called from WalkParticleOverlay._Draw to draw on an additive-blend CanvasItem.</summary>
    internal void DrawWalkModeParticlesOn(CanvasItem canvas)
    {
        if (Particles == null || Grhs == null || Textures == null || Map == null) return;

        foreach (var stream in Particles.Streams)
        {
            if (!stream.Active) continue;
            // Tile center in panel pixels (stream.MapX/Y are 1-based world tiles)
            int dx = stream.MapX - CharX;
            int dy = stream.MapY - CharY;
            // Quick cull: skip streams far from the visible area
            if (Math.Abs(dx) > _halfTilesX + ExtraTilesLarge ||
                Math.Abs(dy) > _halfTilesY + ExtraTilesLarge) continue;

            float streamX = (dx + _halfTilesX) * TileSize + TileSize / 2f + _particleDrawOfsX;
            float streamY = (dy + _halfTilesY) * TileSize + TileSize / 2f + _particleDrawOfsY;

            foreach (var p in stream.Particles)
            {
                if (!p.Alive || p.GrhIndex <= 0 || p.GrhIndex >= Grhs.Length) continue;
                var grh = Grhs[p.GrhIndex];
                if (grh.NumFrames > 1 && grh.Frames != null && grh.Frames.Length > 0)
                {
                    int frame = grh.Speed > 0 ? (int)(_globalTime * grh.NumFrames / grh.Speed) % grh.NumFrames : 0;
                    int frameIdx = grh.Frames[frame];
                    if (frameIdx <= 0 || frameIdx >= Grhs.Length) continue;
                    grh = Grhs[frameIdx];
                }
                if (grh.FileNum <= 0 || grh.PixelWidth <= 0 || grh.PixelHeight <= 0) continue;
                var texture = Textures.GetTexture(grh.FileNum);
                if (texture == null) continue;

                var srcRect = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight);
                float drawX = streamX + p.X - grh.PixelWidth / 2f;
                float drawY = streamY + p.Y - grh.PixelHeight / 2f;
                var destRect = new Rect2(drawX, drawY, grh.PixelWidth, grh.PixelHeight);
                var color = new Color(p.ColR / 255f, p.ColG / 255f, p.ColB / 255f, p.Alpha);
                canvas.DrawTextureRectRegion(texture, destRect, srcRect, color);
            }
        }
    }

    private static bool HasAnyLight(MapData map)
    {
        if (map == null) return false;
        for (int y = 1; y <= map.Height; y++)
            for (int x = 1; x <= map.Width; x++)
                if (map.Tiles[x, y].HasLight) return true;
        return false;
    }

    private GrhData ResolveStaticFrame(int grhIndex)
    {
        var grh = Grhs![grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int resolved = grh.Frames[0];
            if (resolved > 0 && resolved < Grhs.Length)
                return Grhs[resolved];
        }
        return grh;
    }

    private GrhData ResolveFrame(int grhIndex, int frame)
    {
        var grh = Grhs![grhIndex];
        if (grh.NumFrames > 1 && grh.Frames is { Length: > 0 })
        {
            int fi = frame % grh.Frames.Length;
            int resolved = grh.Frames[fi];
            if (resolved > 0 && resolved < Grhs.Length)
                return Grhs[resolved];
        }
        return grh;
    }

    private int GetGrhFrameCount(int grhIndex)
    {
        if (grhIndex <= 0 || grhIndex >= Grhs!.Length) return 1;
        var grh = Grhs[grhIndex];
        return grh.NumFrames > 1 ? grh.NumFrames : 1;
    }

    private static string HeadingName(int h) => h switch
    {
        1 => "Norte", 2 => "Este", 3 => "Sur", 4 => "Oeste", _ => "?"
    };

    // ── Inner class: additive-blend particle overlay ──────────────────────────

    /// <summary>
    /// Transparent overlay drawn on top of the walk-mode panel with additive
    /// blending, so particles emit light correctly (same as ParticleOverlay in
    /// MapViewport). Delegates actual drawing to WalkModePanel.DrawWalkModeParticlesOn.
    /// </summary>
    private sealed partial class WalkParticleOverlay : Control
    {
        public new WalkModePanel? Owner;

        public override void _Draw()
        {
            Owner?.DrawWalkModeParticlesOn(this);
        }
    }
}
