using Godot;
using System;
using System.Collections.Generic;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;
using ArgentumNextgen.Network;
using ArgentumNextgen.Rendering;

namespace ArgentumNextgen.UI;

public partial class AuraViewerPanel : RpgBaseForm
{
	private GameData? _data;
	private AoTcpClient? _tcp;
	private LineEdit? _searchEdit;
	private ItemList? _auraList;
	private AuraPreview? _preview;
	private readonly List<int> _filtered = new();

	public AuraViewerPanel() : base("Visor de Auras", new Vector2(390, 480), "v2") { }

	public void Init(GameData data, AoTcpClient? tcp)
	{
		_data = data;
		_tcp = tcp;
		ApplyFilter("");
	}

	public void SetTcp(AoTcpClient? tcp) => _tcp = tcp;

	protected override void BuildContent()
	{
		var root = RpgTheme.CreateColumn(RpgTheme.SpacingSm);
		ContentContainer.AddChild(root);

		_searchEdit = RpgTheme.CreateRpgInput("Filtrar por numero, grh u offset...");
		_searchEdit.TextChanged += ApplyFilter;
		root.AddChild(_searchEdit);

		var row = RpgTheme.CreateRow(RpgTheme.SpacingSm);
		root.AddChild(row);

		_auraList = RpgTheme.CreateRpgItemList(210, 270);
		_auraList.ItemSelected += OnAuraSelected;
		_auraList.ItemActivated += OnAuraActivated;
		row.AddChild(_auraList);

		_preview = new AuraPreview();
		_preview.CustomMinimumSize = new Vector2(120, 120);
		row.AddChild(_preview);

		var buttons = RpgTheme.CreateRow(RpgTheme.SpacingSm);
		root.AddChild(buttons);

		var applyBtn = RpgTheme.CreateRpgButton("Aplicar", false, 13);
		applyBtn.CustomMinimumSize = new Vector2(90, 28);
		applyBtn.Pressed += ApplySelectedAura;
		buttons.AddChild(applyBtn);

		var clearBtn = RpgTheme.CreateRpgButton("Quitar", false, 13);
		clearBtn.CustomMinimumSize = new Vector2(90, 28);
		clearBtn.Pressed += () => _tcp?.SendPacket(ClientPackets.WriteTalk("/AURA 0"));
		buttons.AddChild(clearBtn);
	}

	public void Open()
	{
		ApplyFilter(_searchEdit?.Text ?? "");
		ShowForm();
	}

	private void ApplyFilter(string filter)
	{
		_filtered.Clear();
		if (_data?.Auras == null || _data.Auras.Length <= 1)
		{
			RefreshList();
			return;
		}

		string lower = filter.Trim().ToLowerInvariant();
		for (int i = 1; i < _data.Auras.Length; i++)
		{
			var aura = _data.Auras[i];
			if (aura.GrhIndex <= 0) continue;
			string haystack = $"{i} {aura.GrhIndex} {aura.Offset} {(aura.Giratoria ? "giratoria" : "")}";
			if (lower.Length == 0 || haystack.Contains(lower, StringComparison.OrdinalIgnoreCase))
				_filtered.Add(i);
		}
		RefreshList();
	}

	private void RefreshList()
	{
		if (_auraList == null) return;
		_auraList.Clear();
		if (_data?.Auras == null) return;

		foreach (int index in _filtered)
		{
			var aura = _data.Auras[index];
			string spin = aura.Giratoria ? " rot" : "";
			_auraList.AddItem($"[{index}] GRH {aura.GrhIndex} off {aura.Offset}{spin}");
		}
	}

	private void OnAuraSelected(long selected)
	{
		if (_data == null || selected < 0 || selected >= _filtered.Count) return;
		int auraIndex = _filtered[(int)selected];
		_preview?.SetAura(_data, auraIndex);
	}

	private void OnAuraActivated(long selected)
	{
		OnAuraSelected(selected);
		ApplySelectedAura();
	}

	private void ApplySelectedAura()
	{
		if (_auraList == null) return;
		var selected = _auraList.GetSelectedItems();
		if (selected.Length == 0 || selected[0] >= _filtered.Count) return;
		int auraIndex = _filtered[selected[0]];
		_tcp?.SendPacket(ClientPackets.WriteTalk($"/AURA {auraIndex}"));
	}

	private partial class AuraPreview : Control
	{
		private GameData? _data;
		private int _auraIndex;

		public void SetAura(GameData data, int auraIndex)
		{
			_data = data;
			_auraIndex = auraIndex;
			QueueRedraw();
		}

		public override void _Draw()
		{
			DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.035f, 0.03f, 0.92f), true);
			DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.55f, 0.38f, 0.12f, 0.8f), false, 1f);
			if (_data?.Auras == null || _auraIndex <= 0 || _auraIndex >= _data.Auras.Length) return;
			var aura = _data.Auras[_auraIndex];
			if (aura.GrhIndex <= 0) return;

			var color = new Color(aura.R / 255f, aura.G / 255f, aura.B / 255f, 1f);
			CharRenderer.DrawGrh(this, _data, aura.GrhIndex, 0, Size / 2f, true, color);
		}
	}
}
