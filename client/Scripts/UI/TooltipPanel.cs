using Godot;
using ArgentumNextgen.Game;

namespace ArgentumNextgen.UI;

/// <summary>
/// Floating tooltip panel that follows the mouse cursor.
/// Shows item/spell information when hovering over slots.
/// Now uses RpgTheme for consistent RPG-styled appearance.
/// </summary>
public partial class TooltipPanel : PanelContainer
{
    private const int MaxWidth = 180;
    private const int OffsetX = 16;
    private const int OffsetY = 16;

    private RichTextLabel? _label;
    private bool _showing;

    private static string GetObjTypeName(int objType) => objType switch
    {
        1 => "Comida", 2 => "Arma", 3 => "Armadura", 4 => "Arbol",
        5 => "Guita", 6 => "Puerta", 7 => "Contenedor", 8 => "Cartel",
        9 => "Llave", 10 => "Foro", 11 => "Pocion", 12 => "Libro",
        13 => "Bebida", 14 => "Leña", 15 => "Fogata", 16 => "Escudo",
        17 => "Casco", 18 => "Anillo", 19 => "Teleport", 20 => "Mueble",
        21 => "Joya", 22 => "Yacimiento", 23 => "Instrumento", 24 => "Yunque",
        25 => "Fragua", 26 => "Instrumento", 27 => "Barco", 28 => "Flecha",
        29 => "Botella Vacia", 30 => "Botella Llena", 31 => "Mancha",
        32 => "Municion", 33 => "Pergamino", 34 => "Cualquiera",
        _ => "Objeto"
    };

    public override void _Ready()
    {
        // RPG-styled tooltip background
        var stylebox = new StyleBoxFlat();
        stylebox.BgColor = new Color(0.06f, 0.05f, 0.04f, 0.95f);
        stylebox.BorderColor = new Color(0.55f, 0.45f, 0.3f, 0.9f);
        stylebox.SetBorderWidthAll(2);
        stylebox.SetCornerRadiusAll(3);
        stylebox.ContentMarginLeft = 8; stylebox.ContentMarginRight = 8;
        stylebox.ContentMarginTop = 6;  stylebox.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", stylebox);

        _label = new RichTextLabel();
        _label.BbcodeEnabled = true;
        _label.FitContent = true;
        _label.ScrollActive = false;
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.CustomMinimumSize = new Vector2(100, 0);
        _label.Size = new Vector2(MaxWidth, 0);
        _label.AddThemeFontSizeOverride("normal_font_size", 10);
        _label.AddThemeFontSizeOverride("bold_font_size", 10);
        _label.AddThemeColorOverride("default_color", new Color(0.85f, 0.8f, 0.65f));
        AddChild(_label);

        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        ZIndex = RpgBaseForm.ZTooltip;
    }

    public void ShowInventoryItem(InventorySlot item)
    {
        if (item.ObjIndex <= 0) { Hide(); return; }
        ShowBbcode(BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, item.Value, item.Amount, item.Equipped));
    }

    public void ShowBankItem(BankItem item)
    {
        if (item.ObjIndex <= 0) { Hide(); return; }
        ShowBbcode(BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, 0, item.Amount, false));
    }

    public void ShowNpcShopItem(NpcShopItem item)
    {
        if (string.IsNullOrEmpty(item.Name)) { Hide(); return; }
        ShowBbcode(BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, (int)item.Price, item.Amount, false));
    }

    public void ShowTradeItem(TradeOfferSlot item)
    {
        if (item.ObjIndex <= 0) { Hide(); return; }
        ShowBbcode(BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, item.Value, item.Amount, false));
    }

    public void ShowSpell(SpellSlot spell)
    {
        if (spell.SpellId <= 0) { Hide(); return; }
        ShowBbcode($"[b][color=cyan]{EscapeBbcode(spell.Name)}[/color][/b]");
    }

    public void ShowGuildBankItem(GuildBankSlot item)
    {
        if (item.ObjIndex <= 0) { Hide(); return; }
        ShowBbcode(BuildItemBbcode(
            item.Name, item.ObjType, item.MinHit, item.MaxHit,
            item.MinDef, item.MaxDef, 0, item.Amount, false));
    }

    public void UpdatePosition()
    {
        if (!_showing) return;

        var mousePos = GetGlobalMousePosition();
        float x = mousePos.X + OffsetX;
        float y = mousePos.Y + OffsetY;

        var viewportSize = GetViewportRect().Size;
        if (x + Size.X > viewportSize.X) x = mousePos.X - Size.X - 4;
        if (y + Size.Y > viewportSize.Y) y = mousePos.Y - Size.Y - 4;
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
        _label.Size = new Vector2(MaxWidth, 0);
        Size = new Vector2(0, 0);
    }

    private static string BuildItemBbcode(
        string name, int objType,
        int minHit, int maxHit, int minDef, int maxDef,
        int value, int amount, bool equipped)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[b][color=yellow]{EscapeBbcode(name)}[/color][/b]");
        sb.Append($"\n[color=gray]{GetObjTypeName(objType)}[/color]");
        if (equipped) sb.Append("  [color=lime][E][/color]");

        bool isWeapon = objType == 2;
        bool isDefensive = objType == 3 || objType == 16 || objType == 17;

        if (isWeapon && (minHit > 0 || maxHit > 0))
            sb.Append($"\n[color=orange]Daño: {minHit}/{maxHit}[/color]");
        if (isDefensive && (minDef > 0 || maxDef > 0))
            sb.Append($"\n[color=dodgerblue]Def: {minDef}/{maxDef}[/color]");
        if (!isWeapon && (minHit > 0 || maxHit > 0))
            sb.Append($"\n[color=orange]Daño: {minHit}/{maxHit}[/color]");
        if (!isDefensive && (minDef > 0 || maxDef > 0))
            sb.Append($"\n[color=dodgerblue]Def: {minDef}/{maxDef}[/color]");
        if (value > 0) sb.Append($"\n[color=gold]Valor: {value}[/color]");
        if (amount > 1) sb.Append($"\n[color=white]x{amount}[/color]");

        return sb.ToString();
    }

    private static string EscapeBbcode(string text) => text.Replace("[", "[lb]");
}
