using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;

namespace ArgentumNextgen.UI;

/// <summary>
/// SOS/Help request panel for GMs.
/// Displays player help requests (/GM messages). GMs can click to teleport to the requester.
/// Populated from server SOS notifications (ShowSOSForm packet or similar).
/// </summary>
public partial class SosPanel : PanelContainer
{
    private const int PanelW = 360;
    private const int PanelH = 350;
    private const int TitleBarH = 28;

    private GameState? _state;
    private AoTcpClient? _tcp;

    // Dragging
    private bool _dragging;
    private Vector2 _dragOffset;

    // Controls
    private ItemList? _sosList;
    private Label? _detailLabel;

    // SOS entries: (playerName, message)
    private readonly List<(string name, string message)> _entries = new();

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
        style.BorderColor = new Color(0.6f, 0.4f, 0.1f, 1f);
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        // Title bar
        var titleBar = new HBoxContainer();
        titleBar.CustomMinimumSize = new Vector2(0, TitleBarH);
        root.AddChild(titleBar);

        var titleLabel = new Label();
        titleLabel.Text = "  Pedidos de Ayuda (SOS)";
        titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
        titleLabel.AddThemeFontSizeOverride("font_size", 13);
        titleBar.AddChild(titleLabel);

        var closeBtn = new Button();
        closeBtn.Text = "X";
        closeBtn.CustomMinimumSize = new Vector2(28, 24);
        closeBtn.Pressed += () => Visible = false;
        titleBar.AddChild(closeBtn);

        // SOS list
        _sosList = new ItemList();
        _sosList.SizeFlagsVertical = SizeFlags.ExpandFill;
        _sosList.CustomMinimumSize = new Vector2(PanelW - 16, 150);
        _sosList.AddThemeFontSizeOverride("font_size", 11);
        _sosList.ItemSelected += OnSosSelected;
        root.AddChild(_sosList);

        // Detail label
        _detailLabel = new Label();
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailLabel.CustomMinimumSize = new Vector2(PanelW - 16, 40);
        _detailLabel.AddThemeFontSizeOverride("font_size", 11);
        _detailLabel.AddThemeColorOverride("font_color", Colors.White);
        _detailLabel.Text = "Seleccione un pedido para ver el detalle.";
        root.AddChild(_detailLabel);

        // Action buttons
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 4);
        root.AddChild(btnRow);

        var goToBtn = new Button();
        goToBtn.Text = "Ir al jugador";
        goToBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        goToBtn.Pressed += OnGoToPressed;
        btnRow.AddChild(goToBtn);

        var removeBtn = new Button();
        removeBtn.Text = "Remover";
        removeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        removeBtn.Pressed += OnRemovePressed;
        btnRow.AddChild(removeBtn);

        var clearBtn = new Button();
        clearBtn.Text = "Limpiar todo";
        clearBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        clearBtn.Pressed += () =>
        {
            _entries.Clear();
            RefreshList();
            if (_detailLabel != null) _detailLabel.Text = "";
        };
        btnRow.AddChild(clearBtn);
    }

    /// <summary>
    /// Add a new SOS request entry. Called when server sends a GM SOS notification.
    /// </summary>
    public void AddSosEntry(string playerName, string message)
    {
        // Avoid duplicates from same player
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
                _entries.RemoveAt(i);
        }
        _entries.Add((playerName, message));
        if (_entries.Count > 50) _entries.RemoveAt(0);
        RefreshList();
    }

    private void RefreshList()
    {
        if (_sosList == null) return;
        _sosList.Clear();
        foreach (var entry in _entries)
            _sosList.AddItem($"{entry.name}: {entry.message}");
    }

    private void OnSosSelected(long index)
    {
        int idx = (int)index;
        if (idx < 0 || idx >= _entries.Count || _detailLabel == null) return;
        var entry = _entries[idx];
        _detailLabel.Text = $"Jugador: {entry.name}\nMensaje: {entry.message}";
    }

    private void OnGoToPressed()
    {
        if (_sosList == null || _tcp == null) return;
        var selected = _sosList.GetSelectedItems();
        if (selected.Length == 0) return;
        int idx = selected[0];
        if (idx < 0 || idx >= _entries.Count) return;
        _tcp.SendPacket(ClientPackets.WriteTalk($"/IRA {_entries[idx].name}"));
    }

    private void OnRemovePressed()
    {
        if (_sosList == null) return;
        var selected = _sosList.GetSelectedItems();
        if (selected.Length == 0) return;
        int idx = selected[0];
        if (idx < 0 || idx >= _entries.Count) return;
        _entries.RemoveAt(idx);
        RefreshList();
        if (_detailLabel != null) _detailLabel.Text = "";
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
