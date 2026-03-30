using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// VB6 frmGuildFoundation — Guild creation form.
/// Fields: clan name, description, URL, 8 codex lines.
/// Sends CIG packet with BF-delimited data.
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class GuildFoundationPanel : RpgBaseForm
{
    private const char BF = '\u00BF';

    private GameState? _state;
    private AoTcpClient? _tcp;

    private LineEdit? _nameEdit;
    private TextEdit? _descEdit;
    private LineEdit? _urlEdit;
    private LineEdit?[] _codexEdits = new LineEdit[8];
    private TextureButton? _createBtn;
    private TextureButton? _cancelBtn;

    public GuildFoundationPanel() : base("Fundar Clan", new Vector2(380, 500), "v2") { }

    public void Init(GameState state, AoTcpClient tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    protected override void BuildContent()
    {
        var scrollArea = RpgTheme.CreateScrollArea(RpgTheme.SpacingMd);
        ContentContainer.AddChild(scrollArea);
        var vbox = scrollArea.GetMeta("content").As<VBoxContainer>();

        // Name
        vbox.AddChild(RpgTheme.CreateInfoLabel("Nombre del clan:", 12));
        _nameEdit = RpgTheme.CreateRpgInput("Solo letras y espacios");
        _nameEdit.MaxLength = 40;
        vbox.AddChild(_nameEdit);

        // Description
        vbox.AddChild(RpgTheme.CreateInfoLabel("Descripcion:", 12));
        _descEdit = RpgTheme.CreateRpgTextEdit("", 0, 60);
        vbox.AddChild(_descEdit);

        // URL
        vbox.AddChild(RpgTheme.CreateInfoLabel("URL (opcional):", 12));
        _urlEdit = RpgTheme.CreateRpgInput("http://...");
        vbox.AddChild(_urlEdit);

        // Codex
        vbox.AddChild(RpgTheme.CreateInfoLabel("Codex (hasta 8 lineas):", 12));
        for (int i = 0; i < 8; i++)
        {
            _codexEdits[i] = RpgTheme.CreateRpgInput($"Linea {i + 1}");
            vbox.AddChild(_codexEdits[i]!);
        }

        // Buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingLg);
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(btnRow);

        _createBtn = RpgTheme.CreateRpgButton("Fundar Clan", false, 13);
        _createBtn.CustomMinimumSize = new Vector2(140, 34);
        _createBtn.Pressed += OnCreate;
        btnRow.AddChild(_createBtn);

        _cancelBtn = RpgTheme.CreateRpgButton("Cancelar", false, 13);
        _cancelBtn.CustomMinimumSize = new Vector2(110, 34);
        _cancelBtn.Pressed += () => HideForm();
        btnRow.AddChild(_cancelBtn);
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
        HideForm();
    }
}
