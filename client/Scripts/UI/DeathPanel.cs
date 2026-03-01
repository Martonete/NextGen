using Godot;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.UI;

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

    private AoTcpClient? _tcp;
    private GameState? _state;

    private Texture2D? _bgTexture;
    private Texture2D? _continuarNormal;
    private Texture2D? _continuarHover;
    private Texture2D? _regresarNormal;
    private Texture2D? _regresarHover;

    private int _hoveredBtn = 0; // 0=none, 1=continuar, 2=regresar

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Size = new Vector2(PanelW, PanelH);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.None;

        string basePath = "res://Data/Graficos/Principal/";
        _bgTexture = LoadJpg(basePath + "cartelMuerte_Main.jpg");
        _continuarHover = LoadJpg(basePath + "cartelMuerte_ContinuarI.jpg");
        _continuarNormal = LoadJpg(basePath + "cartelMuerte_ContinuarA.jpg");
        _regresarHover = LoadJpg(basePath + "cartelMuerte_RegresarI.jpg");
        _regresarNormal = LoadJpg(basePath + "cartelMuerte_RegresarA.jpg");
    }

    private static Texture2D? LoadJpg(string path)
    {
        if (ResourceLoader.Exists(path))
            return ResourceLoader.Load<Texture2D>(path);

        string diskPath = ProjectSettings.GlobalizePath(path);
        if (!FileAccess.FileExists(path) && !System.IO.File.Exists(diskPath))
            return null;

        var img = new Image();
        var err = img.Load(diskPath);
        if (err != Error.Ok) return null;
        return ImageTexture.CreateFromImage(img);
    }

    public void Show()
    {
        _hoveredBtn = 0;
        Visible = true;
    }

    public void Hide()
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
                _tcp?.SendPacket(";/REGRESAR");
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
