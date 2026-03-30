using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmViajar — Travel NPC panel.
/// Background image + 8 city buttons with hover highlight + description text.
/// Clicking a city sends "/VIAJAR {CITY}" and closes the panel.
/// </summary>
public partial class TravelPanel : Control
{
    // VB6 form: ScaleWidth=450, ScaleHeight=350
    private const int PanelW = 450;
    private const int PanelH = 350;

    // Button layout (VB6 twips÷15 → pixels)
    private const int BtnX = 13;
    private const int BtnW = 423;
    private const int BtnH = 20;

    // Close button (VB6 Image1)
    private const int CloseX = 424;
    private const int CloseY = 0;
    private const int CloseW = 25;
    private const int CloseH = 25;

    // Description text area
    private const int DescX = 13;
    private const int DescY = 258;
    private const int DescW = 423;
    private const int DescH = 85;

    private AoTcpClient? _tcp;
    private GameState? _state;

    // City definitions: name, command, Y position, description
    private static readonly CityDef[] Cities = new CityDef[]
    {
        new("Tanaris",    "TANARIS",    42,  "La ciudad donde comienzan tus aventuras, para el sur (mapa 18) se encuentra la entrada a las catacumbas. Llendo para el Norte se llega a la ciudad de Thir. La laguna de Tanaris es un lugar de reunion de numerosos aventureros. Los negocios de esta ciudad vende el equipo mas basico para los novatos."),
        new("Thir",       "THIR",       69,  "Este pequeño pueblo se encuentra en los bosques, al sur esta el bosque de los osos que es un buen lugar para conseguir pieles. Llendo al norte se encuentra el polo. La clase de objetos que se venden aca son los mismo que Tanaris."),
        new("Jhumbel",    "JHUMBEL",    96,  "Este pueblo esta en grupo de islas del mapa 69, es el mejor lugar para ir hacia la peligrosa dungeon del 70. Tiene unos pocos negocios, un cura y un banquero."),
        new("Inthak",     "INTHAK",     123, "Esta ciudad se encuentra en el medio del desierto del sur, cerca de la peligrosa Piramide de Inthak, posee vendedores de pociones, un cura, un banquero y algunos negocios pequeños."),
        new("Anvilmar",   "ANVILMAR",   150, "La ciudad capital de la Alianza Imperial, esta gran ciudad se encuentra en el sur, en el mapa de abajo (Mapa 41) se encuentra el muelle desde donde cada dia parten barcos al peligroso desierto del sur o al castillo 33. Al norte se encuentra otra de las entradas a las Catacumbas. En esta ciudad se vende el mejor equipo disponible a la venta."),
        new("Kahlimdor",  "KAHLIMDOR",  177, "La terrible ciudad central de la Horda Infernal, se encuentra en el norte cerca de la zona de torneos y el polo, en el mapa de la derecha esta otra de las entradas a la catacumbas y bajando por el mar se llega a un peligroso Dungeon. En esta ciudad se vende el mejor equipo disponible a la venta."),
        new("Ruvendel",   "RUVENDEL",   204, "Esta ciudad se encuentra en el mapa 26, al norte se encuentra un volcan y la entrada al dungeon infernal, al sur se encuentra la entrada a la isla y a la cueva de los osos. En esta ciudad hay varios tipos de negocios y un ring de pelea para los guerreros mas valientes."),
        new("Helka",      "HELKA",      231, "Esta ciudad se encuentra en el norte del mundo, posee varios negocios y es una buena zona de partida para los aventureros que quieren explorar el polo."),
    };

    // Loaded textures
    private Texture2D? _bgTexture;
    private readonly Texture2D?[] _btnNormal = new Texture2D?[8];
    private readonly Texture2D?[] _btnHover = new Texture2D?[8];

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // State
    private int _hoveredIdx = -1;
    private string _descText = "";

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
        // Load textures from Principal directory (filesystem)
        string basePath = System.IO.Path.Combine(_dataPath, "Graficos", "Principal");
        _bgTexture = LoadJpg(System.IO.Path.Combine(basePath, "Viajar_Main.jpg"));

        string[] fileNames = { "Tanaris", "Thir", "Jhumbel", "Inthak", "Anvilmar", "Kahlimdor", "Ruvendel", "Helka" };
        for (int i = 0; i < 8; i++)
        {
            _btnNormal[i] = LoadJpg(System.IO.Path.Combine(basePath, $"Viajar_B{fileNames[i]}N.jpg"));
            _btnHover[i] = LoadJpg(System.IO.Path.Combine(basePath, $"Viajar_B{fileNames[i]}I.jpg"));
        }
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

    public void OpenTravel()
    {
        _hoveredIdx = -1;
        _descText = "";
        Visible = true;
    }

    public void CloseTravel()
    {
        Visible = false;
    }

    public override void _Draw()
    {
        // Background image
        if (_bgTexture != null)
            DrawTexture(_bgTexture, Vector2.Zero);
        else
            DrawRect(new Rect2(0, 0, PanelW, PanelH), new Color(0.1f, 0.08f, 0.15f, 0.96f));

        // Draw city buttons
        for (int i = 0; i < 8; i++)
        {
            var tex = (i == _hoveredIdx) ? (_btnHover[i] ?? _btnNormal[i]) : _btnNormal[i];
            if (tex != null)
                DrawTexture(tex, new Vector2(BtnX, Cities[i].Y));
        }

        // Description text (VB6: Palatino Linotype 9.75pt bold gray)
        if (!string.IsNullOrEmpty(_descText))
        {
            var font = ThemeDB.FallbackFont;
            int fontSize = 11;
            // Word-wrap and draw centered in the description area
            DrawDescriptionText(font, fontSize, _descText);
        }
    }

    private void DrawDescriptionText(Font font, int fontSize, string text)
    {
        var color = new Color(0.5f, 0.5f, 0.5f); // #808080
        float maxW = DescW;
        float lineH = fontSize + 2;
        float y = DescY + lineH;

        // Simple word wrap
        string[] words = text.Split(' ');
        string line = "";
        foreach (string word in words)
        {
            string test = string.IsNullOrEmpty(line) ? word : line + " " + word;
            float testW = font.GetStringSize(test, HorizontalAlignment.Left, -1, fontSize).X;
            if (testW > maxW && !string.IsNullOrEmpty(line))
            {
                // Center the line
                float lineW = font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X;
                float cx = DescX + (maxW - lineW) / 2f;
                DrawString(font, new Vector2(cx, y), line, HorizontalAlignment.Left, -1, fontSize, color);
                y += lineH;
                line = word;
                if (y > DescY + DescH) break;
            }
            else
            {
                line = test;
            }
        }
        // Last line
        if (!string.IsNullOrEmpty(line) && y <= DescY + DescH)
        {
            float lineW = font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X;
            float cx = DescX + (maxW - lineW) / 2f;
            DrawString(font, new Vector2(cx, y), line, HorizontalAlignment.Left, -1, fontSize, color);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_tcp == null) return;

        // Dragging by top area (close button area, ~25px)
        if (@event is InputEventMouseButton dragMb)
        {
            if (dragMb.ButtonIndex == MouseButton.Left)
            {
                if (dragMb.Pressed && dragMb.Position.Y <= CloseH && !IsInCloseButton(dragMb.Position))
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

        if (@event is InputEventMouseMotion mm)
        {
            int oldHover = _hoveredIdx;
            _hoveredIdx = HitTestCity(mm.Position);
            if (_hoveredIdx >= 0)
                _descText = Cities[_hoveredIdx].Description;
            else
                _descText = "";
            if (oldHover != _hoveredIdx)
                QueueRedraw();
            // Cursor
            MouseDefaultCursorShape = _hoveredIdx >= 0 || IsInCloseButton(mm.Position)
                ? CursorShape.PointingHand : CursorShape.Arrow;
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Close button
            if (IsInCloseButton(mb.Position))
            {
                CloseTravel();
                AcceptEvent();
                return;
            }

            int idx = HitTestCity(mb.Position);
            if (idx >= 0)
            {
                // Jhumbel: boat warning (VB6 MsgBox)
                if (Cities[idx].Command == "JHUMBEL")
                {
                    // Send directly — the server validates boat requirement
                    GD.Print("[TRAVEL] Warning: Jhumbel requires a boat");
                }

                GD.Print($"[TRAVEL] Traveling to {Cities[idx].Command}");
                _tcp.SendPacket(ClientPackets.WriteTalk($"/VIAJAR {Cities[idx].Command}"));
                CloseTravel();
                AcceptEvent();
                return;
            }

            AcceptEvent();
        }
        else if (@event is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Right)
        {
            CloseTravel();
            AcceptEvent();
        }
    }

    private int HitTestCity(Vector2 pos)
    {
        for (int i = 0; i < 8; i++)
        {
            if (pos.X >= BtnX && pos.X < BtnX + BtnW &&
                pos.Y >= Cities[i].Y && pos.Y < Cities[i].Y + BtnH)
                return i;
        }
        return -1;
    }

    private static bool IsInCloseButton(Vector2 pos)
    {
        return pos.X >= CloseX && pos.X < CloseX + CloseW &&
               pos.Y >= CloseY && pos.Y < CloseY + CloseH;
    }

    private readonly record struct CityDef(string Name, string Command, int Y, string Description);
}
