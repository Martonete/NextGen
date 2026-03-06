using Godot;
using System;
using TierrasSagradasAO.Game;
using TierrasSagradasAO.Network;

namespace TierrasSagradasAO.UI;

/// <summary>
/// VB6 frmGuildFoundation — Guild creation form.
/// Fields: clan name, description, URL, 8 codex lines.
/// Sends CIG packet with BF-delimited data.
/// </summary>
public partial class GuildFoundationPanel : Control
{
    private const int PanelW = 380;
    private const int PanelH = 500;
    private const char BF = '\u00BF';

    private GameState? _state;
    private AoTcpClient? _tcp;

    private bool _dragging;
    private Vector2 _dragOffset;

    private LineEdit? _nameEdit;
    private TextEdit? _descEdit;
    private LineEdit? _urlEdit;
    private LineEdit?[] _codexEdits = new LineEdit[8];
    private Button? _createBtn;
    private Button? _cancelBtn;

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        var bg = new ColorRect();
        bg.Color = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var title = new Label();
        title.Text = "Fundar Clan";
        title.Position = new Vector2(10, 4);
        title.AddThemeFontSizeOverride("font_size", 14);
        AddChild(title);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.Position = new Vector2(PanelW - 28, 2);
        closeBtn.Size = new Vector2(24, 24);
        closeBtn.Pressed += () => Hide();
        AddChild(closeBtn);

        var scroll = new ScrollContainer();
        scroll.Position = new Vector2(8, 30);
        scroll.Size = new Vector2(PanelW - 16, PanelH - 38);
        AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(vbox);

        // Name
        vbox.AddChild(MakeLabel("Nombre del clan:"));
        _nameEdit = new LineEdit();
        _nameEdit.PlaceholderText = "Solo letras y espacios";
        _nameEdit.MaxLength = 40;
        vbox.AddChild(_nameEdit);

        // Description
        vbox.AddChild(MakeLabel("Descripcion:"));
        _descEdit = new TextEdit();
        _descEdit.CustomMinimumSize = new Vector2(0, 60);
        _descEdit.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(_descEdit);

        // URL
        vbox.AddChild(MakeLabel("URL (opcional):"));
        _urlEdit = new LineEdit();
        _urlEdit.PlaceholderText = "http://...";
        vbox.AddChild(_urlEdit);

        // Codex
        vbox.AddChild(MakeLabel("Codex (hasta 8 lineas):"));
        for (int i = 0; i < 8; i++)
        {
            _codexEdits[i] = new LineEdit();
            _codexEdits[i]!.PlaceholderText = $"Linea {i + 1}";
            _codexEdits[i]!.AddThemeFontSizeOverride("font_size", 11);
            vbox.AddChild(_codexEdits[i]!);
        }

        // Buttons
        var btnRow = new HBoxContainer();
        _createBtn = new Button();
        _createBtn.Text = "Fundar Clan";
        _createBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _createBtn.Pressed += OnCreate;
        btnRow.AddChild(_createBtn);

        _cancelBtn = new Button();
        _cancelBtn.Text = "Cancelar";
        _cancelBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cancelBtn.Pressed += () => Hide();
        btnRow.AddChild(_cancelBtn);
        vbox.AddChild(btnRow);
    }

    private static Label MakeLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 11);
        return lbl;
    }

    private void OnCreate()
    {
        if (_tcp == null || _nameEdit == null || _descEdit == null || _urlEdit == null) return;

        string name = _nameEdit.Text.Trim();
        string desc = _descEdit.Text.Trim();
        string url = _urlEdit.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            GD.Print("[Guild] Empty name");
            return;
        }

        // Count non-empty codex lines
        int codexCount = 0;
        for (int i = 0; i < 8; i++)
            if (_codexEdits[i] != null && !string.IsNullOrWhiteSpace(_codexEdits[i]!.Text))
                codexCount = i + 1;

        // Build CIG packet data: desc BF name BF url BF codexCount BF codex1 BF ...
        var data = desc + BF + name + BF + url + BF + codexCount;
        for (int i = 0; i < codexCount; i++)
            data += BF.ToString() + (_codexEdits[i]?.Text ?? "");

        _tcp.SendPacket(ClientPackets.WriteGuildCreate(data));
        Hide();
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < 28)
                {
                    _dragging = true;
                    _dragOffset = mb.GlobalPosition - GlobalPosition;
                }
                else
                    _dragging = false;
            }
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            GlobalPosition = mm.GlobalPosition - _dragOffset;
        }
    }
}
