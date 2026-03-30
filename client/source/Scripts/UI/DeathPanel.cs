using Godot;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmMuertito — Death dialog shown when the player dies.
/// Background: cartelMuerte_Main.jpg (263x100)
/// Two buttons:
///   Image1 = "Continuar" — stay dead as ghost, just close the dialog
///   Image2 = "Regresar"  — send /REGRESAR to server (respawn at hometown), close dialog
/// </summary>
public partial class DeathPanel : Control
{
    private const int PanelW = 263;
    private const int PanelH = 100;

    // VB6 Image1 (Continuar): Left=945/15=63, Top=570/15=38, Width=138, Height=20
    private const int Btn1X = 63;
    private const int Btn1Y = 38;
    private const int BtnW = 138;
    private const int BtnH = 20;

    // VB6 Image2 (Regresar): Left=945/15=63, Top=885/15=59, Width=138, Height=20
    private const int Btn2X = 63;
    private const int Btn2Y = 59;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;
    private const int TitleBarH = 30;

    private AoTcpClient? _tcp;
    private GameState? _state;

    private Texture2D? _bgTexture;
    private Texture2D? _continuarNormal;
    private Texture2D? _continuarHover;
    private Texture2D? _regresarNormal;
    private Texture2D? _regresarHover;

    private int _hoveredBtn = 0; // 0=none, 1=continuar, 2=regresar

    private string _dataPath = "";

    public void Init(GameState state, AoTcpClient tcp, string dataPath)
    {
        _state = state;
        _tcp = tcp;
        _dataPath = dataPath;
        LoadTextures();
    }

    public override void _Ready()
    {
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;
        ZIndex = RpgBaseForm.ZDialog;
    }

    private void LoadTextures()
    {
        string basePath = System.IO.Path.Combine(_dataPath, "Graficos", "Principal");
        _bgTexture = LoadJpg(System.IO.Path.Combine(basePath, "cartelMuerte_Main.jpg"));
        _continuarHover = LoadJpg(System.IO.Path.Combine(basePath, "cartelMuerte_ContinuarI.jpg"));
        _continuarNormal = LoadJpg(System.IO.Path.Combine(basePath, "cartelMuerte_ContinuarA.jpg"));
        _regresarHover = LoadJpg(System.IO.Path.Combine(basePath, "cartelMuerte_RegresarI.jpg"));
        _regresarNormal = LoadJpg(System.IO.Path.Combine(basePath, "cartelMuerte_RegresarA.jpg"));
        QueueRedraw();
    }

    private static Texture2D? LoadJpg(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        var img = new Image();
        var err = img.Load(path);
        if (err != Error.Ok) return null;
        return ImageTexture.CreateFromImage(img);
    }

    public new void Show()
    {
        _hoveredBtn = 0;
        Visible = true;
    }

    public new void Hide()
    {
        Visible = false;
    }

    public override void _Draw()
    {
        if (_bgTexture != null)
            DrawTexture(_bgTexture, Vector2.Zero);
        else
            DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.15f, 0.08f, 0.08f, 0.96f));

        // Continuar button
        var continuarTex = _hoveredBtn == 1 ? (_continuarHover ?? _continuarNormal) : null;
        if (continuarTex != null)
            DrawTexture(continuarTex, new Vector2(Btn1X, Btn1Y));

        // Regresar button
        var regresarTex = _hoveredBtn == 2 ? (_regresarHover ?? _regresarNormal) : null;
        if (regresarTex != null)
            DrawTexture(regresarTex, new Vector2(Btn2X, Btn2Y));
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Dragging by top area
        if (@event is InputEventMouseButton dragMb)
        {
            if (dragMb.ButtonIndex == MouseButton.Left)
            {
                if (dragMb.Pressed && dragMb.Position.Y <= TitleBarH && HitTest(dragMb.Position) == 0)
                {
                    _dragging = true;
                    _dragOffset = dragMb.GlobalPosition - GlobalPosition;
                    AcceptEvent();
                    return;
                }
                if (!dragMb.Pressed && _dragging)
                    _dragging = false;
            }
        }
        if (@event is InputEventMouseMotion dragMm && _dragging)
        {
            GlobalPosition = dragMm.GlobalPosition - _dragOffset;
            AcceptEvent();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            int prev = _hoveredBtn;
            _hoveredBtn = HitTest(motion.Position);
            if (_hoveredBtn != prev) QueueRedraw();
            MouseDefaultCursorShape = _hoveredBtn > 0
                ? CursorShape.PointingHand
                : CursorShape.Arrow;
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int btn = HitTest(mb.Position);
            if (btn == 1)
            {
                // Continuar — just close the dialog, stay dead as ghost
                Hide();
                GD.Print("[DEATH] Continuar — staying as ghost");
            }
            else if (btn == 2)
            {
                // Regresar — send /REGRESAR to server, respawn at hometown
                _tcp?.SendPacket(ClientPackets.WriteTalk("/REGRESAR"));
                Hide();
                GD.Print("[DEATH] Regresar — respawning at hometown");
            }
        }
    }

    private static int HitTest(Vector2 pos)
    {
        if (pos.X >= Btn1X && pos.X < Btn1X + BtnW && pos.Y >= Btn1Y && pos.Y < Btn1Y + BtnH)
            return 1; // Continuar
        if (pos.X >= Btn2X && pos.X < Btn2X + BtnW && pos.Y >= Btn2Y && pos.Y < Btn2Y + BtnH)
            return 2; // Regresar
        return 0;
    }
}
