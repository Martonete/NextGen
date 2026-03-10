using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Floating tooltip panel that follows the mouse cursor.
/// Shows item/spell information when hovering over slots in
/// inventory, bank, commerce, trade, and vault panels.
/// Programmatically created (no .tscn).
/// </summary>
public partial class TooltipPanel : PanelContainer
{
    private const int MaxWidth = 220;
    private const int Padding = 6;
    private const int OffsetX = 16; // offset from cursor
    private const int OffsetY = 16;

    private RichTextLabel? _label;
    private bool _showing;

    // Object type names matching VB6 ObjType enum
    private static string GetObjTypeName(int objType) => objType switch
    {
        1 => "Comida",           // Food/Potion
        2 => "Arma",             // Weapon
        3 => "Armadura",         // Armor
        4 => "Arbol",            // Tree
        5 => "Guita",            // Money
        6 => "Puerta",           // Door
        7 => "Contenedor",       // Container
        8 => "Cartel",           // Sign
        9 => "Llave",            // Key
        10 => "Foro",            // Forum
        11 => "Pocion",          // Potion
        12 => "Libro",           // Book
        13 => "Bebida",          // Drink
        14 => "Leña",            // Firewood
        15 => "Fogata",          // Campfire
        16 => "Escudo",          // Shield
        17 => "Casco",           // Helmet
        18 => "Anillo",          // Ring
        19 => "Teleport",        // Teleport
        20 => "Mueble",          // Furniture
        21 => "Joya",            // Jewel
        22 => "Yacimiento",      // Mineral deposit
        23 => "Instrumento",     // Instrument
        24 => "Yunque",          // Anvil
        25 => "Fragua",          // Forge
        26 => "Instrumento",     // Musical instrument
        27 => "Barco",           // Boat
        28 => "Flecha",          // Arrow
        29 => "Botella Vacia",   // Empty bottle
        30 => "Botella Llena",   // Full bottle
        31 => "Mancha",          // Stain
        32 => "Municion",        // Ammo / Arrow
        33 => "Pergamino",       // Scroll
        34 => "Cualquiera",      // Any
        _ => "Objeto"
    };

    public override void _Ready()
    {
        // Dark semi-transparent background
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.06f, 0.05f, 0.1f, 0.94f);
        stylebox.BorderColor = new Color(0.5f, 0.4f, 0.3f, 0.8f);
        stylebox.SetBorderWidthAll(1);
        stylebox.SetCornerRadiusAll(3);
        stylebox.SetContentMarginAll(Padding);
        AddThemeStyleboxOverride("panel", stylebox);

        _label = new RichTextLabel();
        _label.BbcodeEnabled = true;
        _label.FitContent = true;
        _label.ScrollActive = false;
        _label.CustomMinimumSize = new Vector2(120, 0);
        _label.Size = new Vector2(MaxWidth, 0);
        _label.AddThemeFontSizeOverride("normal_font_size", 11);
        _label.AddThemeFontSizeOverride("bold_font_size", 11);
        _label.AddThemeColorOverride("default_color", Colors.White);
        AddChild(_label);

        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = 100; // always on top
    }

    /// <summary>
    /// Show tooltip for an inventory slot item.
    /// </summary>
    public void ShowInventoryItem(InventorySlot item)
    {
        if (item.ObjIndex <= 0)
        {
            Hide();
            return;
        }

        string bbcode = BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, item.Value, item.Amount, item.Equipped);
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Show tooltip for a bank item.
    /// </summary>
    public void ShowBankItem(BankItem item)
    {
        if (item.ObjIndex <= 0)
        {
            Hide();
            return;
        }

        string bbcode = BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, 0, item.Amount, false);
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Show tooltip for an NPC shop item.
    /// </summary>
    public void ShowNpcShopItem(NpcShopItem item)
    {
        if (string.IsNullOrEmpty(item.Name))
        {
            Hide();
            return;
        }

        string bbcode = BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, (int)item.Price, item.Amount, false);
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Show tooltip for a trade offer slot.
    /// </summary>
    public void ShowTradeItem(TradeOfferSlot item)
    {
        if (item.ObjIndex <= 0)
        {
            Hide();
            return;
        }

        string bbcode = BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, item.Value, item.Amount, false);
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Show tooltip for a spell slot.
    /// </summary>
    public void ShowSpell(SpellSlot spell)
    {
        if (spell.SpellId <= 0)
        {
            Hide();
            return;
        }

        string bbcode = $"[b][color=cyan]{EscapeBbcode(spell.Name)}[/color][/b]";
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Show tooltip for a guild bank slot.
    /// </summary>
    public void ShowGuildBankItem(GuildBankSlot item)
    {
        if (item.ObjIndex <= 0)
        {
            Hide();
            return;
        }

        string bbcode = BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, 0, item.Amount, false);
        ShowBbcode(bbcode);
    }

    /// <summary>
    /// Update tooltip position to follow mouse. Called each frame from Main._Process().
    /// </summary>
    public void UpdatePosition()
    {
        if (!_showing) return;

        var mousePos = GetGlobalMousePosition();
        float x = mousePos.X + OffsetX;
        float y = mousePos.Y + OffsetY;

        // Keep tooltip on screen
        var viewportSize = GetViewportRect().Size;
        if (x + Size.X > viewportSize.X)
            x = mousePos.X - Size.X - 4;
        if (y + Size.Y > viewportSize.Y)
            y = mousePos.Y - Size.Y - 4;
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        GlobalPosition = new Vector2(x, y);
    }

    public new void Hide()
    {
        _showing = false;
        Visible = false;
    }

    private void ShowBbcode(string bbcode)
    {
        if (_label == null) return;
        _label.Text = bbcode;
        _showing = true;
        Visible = true;

        // Force layout update so Size is correct for positioning
        _label.Size = new Vector2(MaxWidth, 0);
        Size = new Vector2(0, 0); // reset so PanelContainer recalculates
    }

    private static string BuildItemBbcode(
        string name, int objType,
        int minHit, int maxHit, int minDef, int maxDef,
        int value, int amount, bool equipped)
    {
        var sb = new System.Text.StringBuilder();

        // Item name (gold/yellow, bold)
        sb.Append($"[b][color=yellow]{EscapeBbcode(name)}[/color][/b]");

        // Type
        string typeName = GetObjTypeName(objType);
        sb.Append($"\n[color=gray]{typeName}[/color]");

        // Equipped badge
        if (equipped)
            sb.Append("  [color=lime][E][/color]");

        // Combat stats — only show non-zero
        bool isWeapon = objType == 2;
        bool isDefensive = objType == 3 || objType == 16 || objType == 17;

        if (isWeapon && (minHit > 0 || maxHit > 0))
            sb.Append($"\n[color=orange]Daño: {minHit}/{maxHit}[/color]");

        if (isDefensive && (minDef > 0 || maxDef > 0))
            sb.Append($"\n[color=dodgerblue]Def: {minDef}/{maxDef}[/color]");

        // Show hit stats on non-weapon items too if present (rings, etc.)
        if (!isWeapon && (minHit > 0 || maxHit > 0))
            sb.Append($"\n[color=orange]Daño: {minHit}/{maxHit}[/color]");

        if (!isDefensive && (minDef > 0 || maxDef > 0))
            sb.Append($"\n[color=dodgerblue]Def: {minDef}/{maxDef}[/color]");

        // Value
        if (value > 0)
            sb.Append($"\n[color=gold]Valor: {value}[/color]");

        // Amount
        if (amount > 1)
            sb.Append($"\n[color=white]x{amount}[/color]");

        return sb.ToString();
    }

    private static string EscapeBbcode(string text)
    {
        return text.Replace("[", "[lb]");
    }
}
