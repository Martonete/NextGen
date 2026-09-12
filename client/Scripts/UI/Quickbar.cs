using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using ArgentumNextgen.Data;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

public partial class Quickbar : Control
{
    public readonly QuickSlot[] Slots = Enumerable.Range(0, 10).Select(i => new QuickSlot { Key = i == 9 ? Key.Key0 : (Key)((long)Key.Key1 + i) }).ToArray();
    public GameState State = null!;
    public GameData Data = null!;
    public Func<int>? SelectedSpell;
    public Func<int>? SelectedObject;
    public Action<QuickSlot, int>? Execute;
    private readonly Button[] _buttons = new Button[10];
    private readonly Label[] _keyLabels = new Label[10];
    private readonly Label[] _slotLabels = new Label[10];
    private readonly TextureRect[] _slotIcons = new TextureRect[10];
    private readonly QuickbarSpellIcon[] _spellIcons = new QuickbarSpellIcon[10];
    private PanelContainer? _menu;
    private Label? _message;
    private int _editing = -1;
    private int _capture = -1;
    private string _profile = "";
    private double _refresh;
    private readonly System.Collections.Generic.Dictionary<int, Texture2D> _icons = new();

    public override void _Ready()
    {
        Size = new Vector2(560, 54);
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            var button = EntryTheme.Button("");
            button.Position = new Vector2(i * 56, 0);
            button.Size = new Vector2(52, 54);
            button.FocusMode = FocusModeEnum.None;
            button.AddThemeFontSizeOverride("font_size", 11);
            button.Pressed += () => OnLeftClick(index);
            button.GuiInput += ev => OnButtonGuiInput(ev, index);
            AddChild(button);
            _buttons[i] = button;
            _keyLabels[i] = EntryTheme.Text("", 10);
            _keyLabels[i].Position = new Vector2(4, 2);
            _keyLabels[i].Size = new Vector2(44, 14);
            _keyLabels[i].TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            button.AddChild(_keyLabels[i]);
            _slotLabels[i] = EntryTheme.Text("+", 12);
            _slotLabels[i].Position = new Vector2(4, 23);
            _slotLabels[i].Size = new Vector2(44, 22);
            _slotLabels[i].HorizontalAlignment = HorizontalAlignment.Center;
            button.AddChild(_slotLabels[i]);
            _slotIcons[i] = new TextureRect { Position = new Vector2(9, 17), Size = new Vector2(34, 34),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore };
            button.AddChild(_slotIcons[i]);
            _spellIcons[i] = new QuickbarSpellIcon { Position = new Vector2(9, 17), Size = new Vector2(34, 34), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            button.AddChild(_spellIcons[i]);
        }
    }

    public override void _Process(double delta)
    {
        if (!State.IsLogged) { CloseMenu(); State.QuickbarKeys.Clear(); _profile = ""; return; }
        if (!IsVisibleInTree()) CloseMenu();
        string profile = State.AccountName + "/" + State.UserName;
        if (_profile != profile) { _profile = profile; LoadSlots(); }
        _refresh += delta;
        if (_refresh < 0.2) return;
        _refresh = 0;
        RefreshClaimedKeys();
        for (int i = 0; i < 10; i++)
        {
            var s = Slots[i];
            var b = _buttons[i];
            int resolved = s.Resolve(State);
            string key = OS.GetKeycodeString(s.Key);
            _keyLabels[i].Text = key;
            _slotLabels[i].Text = s.Id == 0 ? "+" : s.Spell ? s.Name[..Math.Min(3, s.Name.Length)].ToUpperInvariant() : "";
            b.TooltipText = s.Id == 0 ? "Asignar hechizo u objeto" : $"{s.Name} · {key}\nClick: {(s.Spell ? "lanzar" : "usar")} · Click derecho: configurar";
            b.Modulate = s.Id != 0 && resolved < 0 ? new Color(0.55f, 0.55f, 0.55f) : Colors.White;
            _slotIcons[i].Texture = null;
            _spellIcons[i].Visible = s.Spell && s.Id > 0;
            _spellIcons[i].Spell = s.Name;
            int iconId = s.Spell ? 0 : s.Id;
            if (iconId > 0 && iconId < Data.Objects.Length)
            {
                if (!_icons.TryGetValue(iconId, out var icon))
                {
                    var grh = Data.ResolveGrh(Data.Objects[iconId].GrhIndex, 0);
                    var tex = grh == null ? null : Data.Textures?.GetTexture(grh.FileNum);
                    if (grh != null && tex != null)
                        _icons[iconId] = icon = new AtlasTexture { Atlas = tex, Region = new Rect2(grh.SX, grh.SY, grh.PixelWidth, grh.PixelHeight) };
                }
                _slotIcons[i].Texture = icon;
                _slotLabels[i].Visible = icon == null;
            }
            else _slotLabels[i].Visible = true;
            if (_spellIcons[i].Visible) _slotLabels[i].Visible = false;
        }
    }

    /// <summary>Left click: run a loaded slot instantly (item → equip/use, spell → cast).
    /// An empty slot opens the assign menu, same as before — there's nothing to run yet.</summary>
    private void OnLeftClick(int index)
    {
        if (!State.IsLogged || State.AnyFormOpen) return;
        var slot = Slots[index];
        if (slot.Id <= 0) { OpenMenu(index); return; }
        TryExecute(slot);
    }

    private void OnButtonGuiInput(InputEvent ev, int index)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
        {
            if (!State.IsLogged || State.AnyFormOpen) return;
            OpenMenu(index);
            _buttons[index].AcceptEvent();
        }
    }

    /// <summary>Shared by left-click and the keyboard shortcut: resolve the slot and fire it.</summary>
    private void TryExecute(QuickSlot slot)
    {
        int index = slot.Resolve(State);
        if (index >= 0) Execute?.Invoke(slot, index);
        else State.EnqueueChat(new ChatMessage { Text = $"{slot.Name}: no está disponible.", Color = "FFFF00" });
    }

    private void OpenMenu(int index)
    {
        CloseMenu();
        _editing = index;
        _menu = new PanelContainer { ZIndex = 10, Position = new Vector2(Math.Min(index * 56, 250), -280), Size = new Vector2(300, 270) };
        _menu.AddThemeStyleboxOverride("panel", EntryTheme.Box());
        AddChild(_menu);
        // Keep the popover inside the screen even when the bar is moved to the top.
        if (_menu.GlobalPosition.Y < 0) _menu.Position = new Vector2(_menu.Position.X, 58);
        var column = RpgTheme.CreateColumn(4);
        _menu.AddChild(column);
        _message = EntryTheme.Text($"Slot {index + 1} · elegí una acción", 12);
        column.AddChild(_message);
        void Add(string title, Action action)
        {
            var b = EntryTheme.Button(title); b.CustomMinimumSize = new Vector2(0, 28);
            b.AddThemeFontSizeOverride("font_size", 12); column.AddChild(b); b.Pressed += action;
        }
        Add("Equipar hechizo seleccionado", () => Assign(true));
        Add("Equipar objeto seleccionado", () => Assign(false));
        Add("Cambiar tecla", () => { _capture = index; State.QuickbarEditing = true; _message.Text = "Presioná una tecla · Escape cancela"; });
        Add("Vaciar slot", () => { Slots[index].Id = 0; Slots[index].Name = ""; SaveSlots(); CloseMenu(); });
        Add("Cerrar", CloseMenu);
        Callable.From(() =>
        {
            if (_menu == null) return;
            var area = GetViewportRect().Size;
            var menuSize = _menu.GetGlobalRect().Size;
            _menu.GlobalPosition = new Vector2(Mathf.Clamp(_menu.GlobalPosition.X, 0, Math.Max(0, area.X - menuSize.X)),
                Mathf.Clamp(_menu.GlobalPosition.Y, 0, Math.Max(0, area.Y - menuSize.Y)));
        }).CallDeferred();
    }

    private void Assign(bool spell)
    {
        int slot = (spell ? SelectedSpell : SelectedObject)?.Invoke() ?? -1;
        int id = spell ? (slot >= 0 && slot < State.Spells.Length ? State.Spells[slot]?.SpellId ?? 0 : 0)
            : (slot >= 0 && slot < State.Inventory.Length && State.Inventory[slot]?.Amount > 0 ? State.Inventory[slot].ObjIndex : 0);
        if (id <= 0) { _message!.Text = "Seleccioná en la lista y volvé a pulsar."; return; }
        var target = Slots[_editing];
        target.Spell = spell; target.Id = id;
        target.Name = spell ? State.Spells[slot].Name : State.Inventory[slot].Name;
        SaveSlots(); CloseMenu();
    }

    public bool IsOver(Vector2 point) => IsVisibleInTree() && (GetGlobalRect().HasPoint(point) || (_menu != null && _menu.GetGlobalRect().HasPoint(point)));

    public bool HandleKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo) return false;
        if (_capture >= 0)
        {
            if (key.Keycode == Key.Escape) { CloseMenu(); return true; }
            if (Reserved(key.Keycode) || key.CtrlPressed || key.AltPressed || key.MetaPressed || key.ShiftPressed)
            { _message!.Text = "Tecla ocupada o reservada; elegí otra."; return true; }
            if (Slots.Where((s, i) => i != _capture).Any(s => s.Key == key.Keycode))
            { _message!.Text = "Esa tecla ya está en otro slot."; return true; }
            Slots[_capture].Key = key.Keycode; SaveSlots(); CloseMenu(); return true;
        }
        if (key.Keycode == Key.Escape && _menu != null) { CloseMenu(); return true; }
        if (!State.IsLogged || State.ChatActive || State.AnyFormOpen || State.Paused || State.Dead
            || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit
            || key.CtrlPressed || key.AltPressed || key.MetaPressed || key.ShiftPressed) return false;
        foreach (var slot in Slots)
        {
            if (slot.Id <= 0 || slot.Key != key.Keycode || Reserved(slot.Key)) continue;
            TryExecute(slot);
            return true;
        }
        return false;
    }

    private bool Reserved(Key key) => key == Key.None || key is Key.Enter or Key.KpEnter or Key.Escape or Key.Tab or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta or Key.M or Key.P
        or Key.Up or Key.Down or Key.Left or Key.Right || (key >= Key.F1 && key <= Key.F12)
        || (key >= Key.Kp0 && key <= Key.Kp9) || State.Keys.Binds.Any(b => b.KeyCode == key);

    private string ProfilePath => ProjectSettings.GlobalizePath("user://quickbar-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_profile)))[..20] + ".cfg");
    private void LoadSlots()
    {
        CloseMenu();
        var cfg = new ConfigFile(); var result = cfg.Load(ProfilePath);
        for (int i = 0; i < 10; i++)
        {
            string section = i.ToString(); var slot = Slots[i];
            slot.Id = result == Error.Ok ? Math.Max(0, (int)cfg.GetValue(section, "id", 0)) : 0;
            slot.Spell = (bool)cfg.GetValue(section, "spell", false);
            slot.Name = (string)cfg.GetValue(section, "name", "");
            slot.Key = (Key)(long)cfg.GetValue(section, "key", (long)(i == 9 ? Key.Key0 : (Key)((long)Key.Key1 + i)));
        }
        RefreshClaimedKeys();
    }
    private void SaveSlots()
    {
        RefreshClaimedKeys();
        var cfg = new ConfigFile();
        for (int i = 0; i < 10; i++)
        {
            string section = i.ToString(); var slot = Slots[i];
            cfg.SetValue(section, "id", slot.Id); cfg.SetValue(section, "spell", slot.Spell);
            cfg.SetValue(section, "name", slot.Name); cfg.SetValue(section, "key", (long)slot.Key);
        }
        if (cfg.Save(ProfilePath) != Error.Ok) State.EnqueueChat(new ChatMessage { Text = "No se pudo guardar la barra de accesos.", Color = "FFFF00" });
    }
    private void CloseMenu()
    {
        _menu?.QueueFree(); _menu = null; _capture = -1; State.QuickbarEditing = false;
    }
    private void RefreshClaimedKeys()
    {
        State.QuickbarKeys.Clear();
        foreach (var slot in Slots) if (slot.Id > 0 && !Reserved(slot.Key)) State.QuickbarKeys.Add(slot.Key);
    }
    public override void _ExitTree() { State.QuickbarEditing = false; State.QuickbarKeys.Clear(); }
}
