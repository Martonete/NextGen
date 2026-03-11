using Godot;
using System;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// Guild alignment picker panel — shown during guild foundation.
/// Player picks one of 5 faction alignments: Neutral, Royal, Chaos, Civilian, Criminal.
/// Each button sends the alignment choice to the server.
/// </summary>
public partial class GuildAlignmentPanel : PanelContainer
{
    private const int PanelW = 300;
    private const int PanelH = 220;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    /// <summary>
    /// Callback raised when an alignment is selected. Passes alignment index (0-4).
    /// </summary>
    public Action<int>? OnAlignmentChosen;

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(PanelW, PanelH);
        Size = new Vector2(PanelW, PanelH);

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
        style.BorderColor = new Color(0.4f, 0.35f, 0.25f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Alineacion del Clan";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += () => Visible = false;
        titleBar.AddChild(closeBtn);

        // Instructions
        var infoLabel = new Label();
        infoLabel.Text = "Seleccione la alineacion para su nuevo clan:";
        infoLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        infoLabel.AddThemeColorOverride("font_color", Colors.White);
        root.AddChild(infoLabel);

        // Alignment buttons
        string[] alignNames = { "Neutral", "Armada Real", "Fuerzas del Caos", "Civil", "Criminal" };
        Color[] alignColors =
        {
            new Color(0.7f, 0.7f, 0.7f),
            new Color(0.2f, 0.5f, 1f),
            new Color(1f, 0.2f, 0.2f),
            new Color(0.3f, 0.8f, 0.3f),
            new Color(0.8f, 0.4f, 0.1f),
        };

        for (int i = 0; i < alignNames.Length; i++)
        {
            int idx = i;
            var btn = new Button();
            btn.Text = alignNames[i];
            btn.CustomMinimumSize = new Vector2(0, 28);
            btn.AddThemeColorOverride("font_color", alignColors[i]);
            btn.Pressed += () => OnAlignmentSelected(idx);
            root.AddChild(btn);
        }
    }

    private void OnAlignmentSelected(int index)
    {
        // Send alignment choice to server via slash command
        // VB6: /ALINEACION <index> during guild creation
        _tcp?.SendPacket(ClientPackets.WriteTalk($"/ALINEACION {index}"));
        OnAlignmentChosen?.Invoke(index);
        Visible = false;
    }

    public void Open()
    {
        Visible = true;
    }

    // ── Dragging ──────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && mb.Position.Y < TitleBarH)
                {
                    _dragging = true;
                    _dragOffset = mb.Position;
                    AcceptEvent();
                }
                else if (!mb.Pressed)
                    _dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && _dragging)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }
}
