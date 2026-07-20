#nullable enable
using System;
using System.IO;
using Godot;
using AOWorldEditor.Data;

namespace AOWorldEditor.Editor;

/// <summary>
/// Read-mostly secondary window: lets the user view another map, pan/zoom, and
/// select+copy an area from it while the main editor window stays untouched.
/// Deliberately NOT a second editable tab — see plan doc for why (370+ direct
/// references to _map/_state/_viewport/_undo throughout EditorMain made a full
/// session-based multi-tab refactor too risky). This window owns its own MapData
/// + EditorState + UndoManager (never actually used, MapViewport just requires a
/// non-null instance) + MapViewport, but its EditorState.ActiveTool is only ever
/// Hand or Select — no palette, no paint/erase/block, so nothing here can dirty
/// or corrupt the aux map on disk.
///
/// Ctrl+C here writes into the MAIN window's EditorState.Clipboard (passed in as
/// mainClipboardState), not its own — so the existing Ctrl+V flow in EditorMain
/// (PasteClipboard, which always reads _state.Clipboard) picks it up unchanged.
/// </summary>
public partial class AuxMapWindow : Window
{
    private readonly GrhData[] _grhs;
    private readonly TextureManager _textures;
    private readonly string _mapDir;
    private readonly EditorState _mainClipboardState;
    private readonly int[]? _npcBodies;
    private readonly int[]? _npcHeads;
    private readonly int[]? _npcBodyGrhs;
    private readonly int[]? _npcHeadOfsX;
    private readonly int[]? _npcHeadOfsY;
    private readonly int[]? _headGrhs;
    private readonly NpcDatabase? _npcDb;

    private readonly EditorState _auxState = new();
    private readonly UndoManager _auxUndo = new();
    private MapData? _auxMap;
    private MapViewport? _auxViewport;
    private Label? _titleLabel;
    private Label? _statusLabel;

    public AuxMapWindow(
        GrhData[] grhs,
        TextureManager textures,
        string mapDir,
        EditorState mainClipboardState,
        int[]? npcBodies = null,
        int[]? npcHeads = null,
        int[]? npcBodyGrhs = null,
        int[]? npcHeadOfsX = null,
        int[]? npcHeadOfsY = null,
        int[]? headGrhs = null,
        NpcDatabase? npcDb = null)
    {
        _grhs = grhs;
        _textures = textures;
        _mapDir = mapDir;
        _mainClipboardState = mainClipboardState;
        _npcBodies = npcBodies;
        _npcHeads = npcHeads;
        _npcBodyGrhs = npcBodyGrhs;
        _npcHeadOfsX = npcHeadOfsX;
        _npcHeadOfsY = npcHeadOfsY;
        _headGrhs = headGrhs;
        _npcDb = npcDb;
        _auxState.ActiveTool = EditorTool.Select;
        // This window exists to see tiles clearly and copy them — night lighting/fog
        // overlays (on by default in the main editor) would just darken the view for
        // no benefit here. User can still re-enable them from the aux state if needed.
        _auxState.ShowLights = false;
        _auxState.ShowFog = false;
    }

    public override void _Ready()
    {
        Title = "Mapa de Consulta";
        Size = new Vector2I(900, 700);
        Exclusive = false;
        CloseRequested += () => QueueFree();

        // Window's default backdrop reads as near-black before a map is loaded —
        // match the editor's panel background instead.
        var bg = new ColorRect { Color = EditorTheme.BG_PANEL };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 8);
        toolbar.CustomMinimumSize = new Vector2(0, 36);
        toolbar.AddChild(EditorTheme.MakeLabel("Mapa:", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM));

        var mapSpin = new SpinBox { MinValue = 1, MaxValue = 9999, Step = 1 };
        mapSpin.AddThemeFontSizeOverride("font_size", EditorTheme.FONT_SM);
        mapSpin.CustomMinimumSize = new Vector2(80, 0);
        toolbar.AddChild(mapSpin);
        toolbar.AddChild(EditorTheme.MakeButton("Abrir", () => LoadMap((int)mapSpin.Value)));

        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        toolbar.AddChild(EditorTheme.MakeButton("Mano", () => _auxState.ActiveTool = EditorTool.Hand));
        toolbar.AddChild(EditorTheme.MakeButton("Seleccionar", () => _auxState.ActiveTool = EditorTool.Select));
        toolbar.AddChild(EditorTheme.MakeButton("Encuadrar (Home)", () => _auxViewport?.ZoomToFit()));
        toolbar.AddChild(EditorTheme.MakeButton("Copiar (Ctrl+C)", CopySelectionToMainClipboard));

        toolbar.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        _titleLabel = EditorTheme.MakeLabel("(sin mapa)", EditorTheme.TEXT_PRIMARY, EditorTheme.FONT_SM);
        toolbar.AddChild(_titleLabel);
        root.AddChild(toolbar);

        _auxViewport = new MapViewport
        {
            Map = null,
            Grhs = _grhs,
            Textures = _textures,
            State = _auxState,
            Undo = _auxUndo,
            NpcBodies = _npcBodies,
            NpcHeads = _npcHeads,
            NpcBodyGrhs = _npcBodyGrhs,
            NpcHeadOfsX = _npcHeadOfsX,
            NpcHeadOfsY = _npcHeadOfsY,
            HeadGrhs = _headGrhs,
            NpcDb = _npcDb,
            ClipContents = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        // Selecting an area here is the whole point of this window — surface it
        // via status text instead of silently updating HasSelection only.
        _auxViewport.OnSelectionCompleted += () =>
            SetStatus(_auxState.HasSelection
                ? $"Selección: {_auxState.SelX2 - _auxState.SelX1 + 1}x{_auxState.SelY2 - _auxState.SelY1 + 1} — Ctrl+C para copiar"
                : "");
        root.AddChild(_auxViewport);

        _statusLabel = EditorTheme.MakeLabel("", EditorTheme.TEXT_SECONDARY, EditorTheme.FONT_SM);
        _statusLabel.CustomMinimumSize = new Vector2(0, 22);
        root.AddChild(_statusLabel);
    }

    public void LoadMap(int mapNumber)
    {
        if (string.IsNullOrEmpty(_mapDir) || !Directory.Exists(_mapDir))
        {
            SetStatus($"Carpeta de mapas no configurada");
            return;
        }
        string aomapFile = Path.Combine(_mapDir, $"Mapa{mapNumber}.aomap");
        string mapFile = Path.Combine(_mapDir, $"Mapa{mapNumber}.map");
        if (!File.Exists(aomapFile) && !File.Exists(mapFile))
        {
            SetStatus($"Mapa {mapNumber} no existe en {_mapDir}");
            return;
        }

        _auxMap = MapLoader.Load(_mapDir, mapNumber);
        _auxState.ClearSelection();
        _auxState.Zoom = 1f;
        _auxState.CameraOffset = Vector2.Zero;
        if (_auxViewport != null)
        {
            _auxViewport.Map = _auxMap;
            _auxViewport.QueueRedraw();
            Callable.From(() => _auxViewport.ZoomToFit()).CallDeferred();
        }
        if (_titleLabel != null)
            _titleLabel.Text = $"Mapa {mapNumber} — {_auxMap.Name} ({_auxMap.Width}x{_auxMap.Height})";
        SetStatus($"Mapa {mapNumber} cargado (solo consulta — Seleccionar + Ctrl+C para copiar)");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, CtrlPressed: true } key && key.Keycode == Key.C)
        {
            CopySelectionToMainClipboard();
            GetViewport().SetInputAsHandled();
        }
    }

    private void CopySelectionToMainClipboard()
    {
        if (_auxMap == null || !_auxState.HasSelection)
        {
            SetStatus("Nada seleccionado para copiar");
            return;
        }
        _mainClipboardState.CopyRegionFrom(_auxMap, _auxState.SelX1, _auxState.SelY1, _auxState.SelX2, _auxState.SelY2);
        SetStatus($"Copiado {_mainClipboardState.ClipWidth}x{_mainClipboardState.ClipHeight} tiles — Ctrl+V en la ventana principal para pegar");
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.Text = text;
    }
}
