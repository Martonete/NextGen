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
/// Now uses RpgBaseForm for consistent RPG styling.
/// </summary>
public partial class SosPanel : RpgBaseForm
{
    private GameState? _state;
    private AoTcpClient? _tcp;

    // Controls
    private ItemList? _sosList;
    private Label? _detailLabel;

    // SOS entries: (playerName, message)
    private readonly List<(string name, string message)> _entries = new();

    public SosPanel() : base("Pedidos de Ayuda (SOS)", new Vector2(360, 350), "v2") { }

    public void Init(GameState state, AoTcpClient? tcp)
    {
        _state = state;
        _tcp = tcp;
    }

    public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

    protected override void BuildContent()
    {
        var vbox = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
        ContentContainer.AddChild(vbox);

        // SOS list
        _sosList = RpgTheme.CreateRpgItemList(0, 150);
        _sosList.ItemSelected += OnSosSelected;
        vbox.AddChild(_sosList);

        // Detail label
        _detailLabel = RpgTheme.CreateInfoLabel("Seleccione un pedido para ver el detalle.", 11);
        _detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailLabel.CustomMinimumSize = new Vector2(0, 40);
        _detailLabel.AddThemeColorOverride("font_color", Colors.White);
        vbox.AddChild(_detailLabel);

        // Action buttons
        var btnRow = RpgTheme.CreateRow(RpgTheme.SpacingSm);
        vbox.AddChild(btnRow);

        var goToBtn = RpgTheme.CreateRpgButton("Ir al jugador", false, 12);
        goToBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        goToBtn.CustomMinimumSize = new Vector2(0, 30);
        goToBtn.Pressed += OnGoToPressed;
        btnRow.AddChild(goToBtn);

        var removeBtn = RpgTheme.CreateRpgButton("Remover", false, 12);
        removeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        removeBtn.CustomMinimumSize = new Vector2(0, 30);
        removeBtn.Pressed += OnRemovePressed;
        btnRow.AddChild(removeBtn);

        var clearBtn = RpgTheme.CreateRpgButton("Limpiar todo", false, 12);
        clearBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        clearBtn.CustomMinimumSize = new Vector2(0, 30);
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
        ShowForm();
    }
}
