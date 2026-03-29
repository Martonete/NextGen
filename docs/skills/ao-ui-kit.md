---
name: ao-ui-kit
description: Use when creating new UI panels/dialogs, modifying form styles, or working with RpgBaseForm/RpgTheme factory methods in the Godot client.
---

# AO UI Kit Reference

Source: `client/Scripts/UI/RpgBaseForm.cs`, `RpgTheme.cs`

Source: `client/Scripts/UI/RpgBaseForm.cs`, `client/Scripts/UI/RpgTheme.cs`

---

## 1. RpgBaseForm — Base Dialog Class

All in-game dialog windows extend `RpgBaseForm : Control`.

### Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| TitleText | string | "Formulario" | Title bar text |
| FormSize | Vector2 | (500, 400) | Window dimensions |
| FormStyle | string | "v1" | Visual style (v1/v2/v3/v4) |
| Draggable | bool | true | Window can be dragged |
| ShowCloseButton | bool | true | Show X button |
| CloseOnEscape | bool | false | Escape key closes window |
| ContentContainer | MarginContainer | (auto) | Container for child content |

### Constructor

```csharp
// Default constructor (FormStyle defaults to "v1")
new RpgBaseForm()

// Parameterized constructor (FormStyle defaults to "v2")
new RpgBaseForm(title: "My Form", size: new Vector2(600, 500), style: "v2")
```

Note: The default constructor uses style "v1", the parameterized constructor
defaults to "v2".

### Lifecycle

1. `_Ready()` -> registers in `_allForms` list, calls `BuildForm()`
2. `BuildForm()` -> builds style layers, calls `BuildContent()`
3. `BuildContent()` -> **override this** in subclasses to add UI
4. `_ExitTree()` -> removes from `_allForms`

### Show / Hide / Toggle

```csharp
form.ShowForm();                      // centered on viewport
form.ShowForm(new Vector2(100, 50));  // at specific position
form.HideForm();                      // hide
form.Toggle();                        // show/hide toggle
```

`ShowForm()` applies global alpha, centers on viewport, and calls `MoveToFront()`.

---

## 2. Four Visual Styles

### v1 — Info Window Frame + Title

- Layer 0: Solid dark background `(0.10, 0.09, 0.08, 1.0)`
- Layer 1: NinePatch `info_window.png` with margins `(16, 16, 16, 16)`
- Title bar: `name_frame_mid_ready.png` centered on top border
- Content margins: Form margins (top=58, left=30, right=30, bottom=20)

### v2 — Big Bar Stretched

- Background: `big_bar.png` stretched to fill (ExpandMode=IgnoreSize, Scale)
- Title bar: `name_frame_mid_ready.png` centered on top border
- Content margins: Thicker borders (top=54, left=36, right=36, bottom=38)

### v3 — Dark Background + Info Window

- Layer 0: Slightly lighter solid bg `(0.15, 0.14, 0.13, 1.0)`
- Layer 1: NinePatch `info_window.png` with margins `(16, 16, 16, 16)`
- Title bar: `name_frame_mid_ready.png` centered on top border
- Content margins: Form margins (top=58, left=30, right=30, bottom=20)

### v4 — Frame Only (No Title)

- Layer 0: Solid dark background `(0.10, 0.09, 0.08, 1.0)`
- Layer 1: NinePatch `info_window.png` with margins `(16, 16, 16, 16)`
- **No title bar**
- Content margins: Panel margins (top=20, left=30, right=30, bottom=20)

---

## 3. Z-Index Layers

Standardized across the entire game UI:

| Constant | Value | Purpose |
|----------|-------|---------|
| ZPanel | 1 | Normal panels and forms |
| ZTooltip | 2 | Hover tooltips |
| ZContextMenu | 5 | Right-click context menus |
| ZDialog | 10 | Modal dialogs |
| ZLoading | 20 | Loading screens |
| ZBlind | 100 | Full-screen blind overlay |

---

## 4. Global Transparency System

All forms share a global alpha value (0.4 - 1.0):

```csharp
RpgBaseForm.ApplyGlobalAlpha(0.8f);  // set all forms to 80% opacity
```

- Applied via `Modulate = new Color(1, 1, 1, _globalFormAlpha)`
- Clamped to range `[0.4, 1.0]`
- Iterates all live forms and updates them immediately
- Auto-cleans dead form references during iteration

---

## 5. Title Frame

Title bar is a `name_frame_mid_ready.png` NinePatch badge centered on the
form's top border, straddling above and below:

- Width: `max(textLen * 14 + 80, 220)` pixels
- Height: 50 pixels
- Position: centered horizontally, -8px above top edge, +42px below
- NinePatch margins: `(30, 10, 30, 10)`
- Text: 16px title label centered inside

---

## 6. RpgTheme — Centralized Theme Factory

Static utility class. No Node/autoload required.

### Spacing Constants

| Constant | Value | Usage |
|----------|-------|-------|
| SpacingSm | 4 | Tight spacing (within groups) |
| SpacingMd | 8 | Default separation |
| SpacingLg | 12 | Between sections |
| SpacingXl | 16 | Card content margins |

### Form Margins (with title bar)

| Constant | Value |
|----------|-------|
| FormMarginTop | 58 |
| FormMarginLeft | 30 |
| FormMarginRight | 30 |
| FormMarginBottom | 20 |

### Panel Margins (no title bar)

| Constant | Value |
|----------|-------|
| PanelMarginTop | 20 |
| PanelMarginLeft | 30 |
| PanelMarginRight | 30 |
| PanelMarginBottom | 20 |

### Texture Cache

Two caches (populated lazily):
- `_texCache`: raw `Texture2D` by filename
- `_scaledTexCache`: resized `ImageTexture` by filename+size

`GetTex(filename)` tries `res://Data/UI/{filename}` first, then falls back to
the globalized filesystem path.

---

## 7. Panel Styles Registry

Predefined panel backgrounds in `RpgTheme.PanelStyles`:

| Key | Type | Asset | NinePatch Margins |
|-----|------|-------|-------------------|
| big_bar | Stretched | big_bar.png | (none) |
| info_window | NinePatch | info_window.png | (16, 16, 16, 16) |
| info_window_2 | NinePatch | info_window_2.png | (16, 16, 16, 16) |
| dark_card | NinePatch | little_background_frame.png | (10, 10, 10, 10) |
| dialoge | NinePatch | dialoge_frame.png | (16, 16, 16, 16) |
| inventory | NinePatch | inventory_frame.png | (16, 16, 16, 16) |
| skill | NinePatch | skill_frame.png | (12, 12, 12, 12) |
| thin | NinePatch | Thin_frame.png | (8, 8, 8, 8) |
| red_vert | NinePatch | Red_vert_panel.png | (12, 12, 12, 12) |

---

## 8. Mini Buttons

12 mini icon buttons defined in `RpgTheme.MiniButtons`:

| Key | Normal | Hover |
|-----|--------|-------|
| exit | Mini_exit.png | Mini_exit_t.png |
| add | Mini_add.png | Mini_add_t.png |
| help | Mini_help.png | Mini_help_t.png |
| guild | Mini_guild.png | Mini_guild_t.png |
| skip | Mini_skip.png | Mini_skip_t.png |
| speak | Mini_speak.png | Mini_speak_t.png |
| arrow_top | Mini_arrow_top.png | Mini_arrow_top.png |
| arrow_bot | Mini_arrow_bot.png | Mini_arrow_bot.png |
| arrow_top2 | Mini_arrow_top2.png | Mini_arrow_top2_t.png |
| arrow_bot2 | Mini_arrow_bot2.png | Mini_arrow_bot2_t.png |
| arrow_left2 | Mini_arrow_left2.png | Mini_arrow_left2_t.png |
| arrow_right2 | Mini_arrow_right2.png | Mini_arrow_right2_t.png |

---

## 9. Layout Factory Methods

### Containers

```csharp
RpgTheme.CreateRow(spacing);           // HBoxContainer, ExpandFill horizontal
RpgTheme.CreateColumn(spacing);         // VBoxContainer, ExpandFill horizontal
RpgTheme.CreateGrid(columns, hSp, vSp); // GridContainer with spacing
RpgTheme.CreateSection(title, spacing); // VBoxContainer with title label
```

### Scroll Area

```csharp
var wrapper = RpgTheme.CreateScrollArea(spacing);
var content = wrapper.GetMeta("content").As<VBoxContainer>();  // add children here
var scroll = wrapper.GetMeta("scroll").As<ScrollContainer>();  // access scroll
```

Features a custom-themed scrollbar that auto-shows/hides and adjusts margins.

### Helpers

```csharp
RpgTheme.FillParent(control);          // set anchors to full rect
RpgTheme.ExpandH(control);             // SizeFlags.ExpandFill horizontal
RpgTheme.ExpandV(control);             // SizeFlags.ExpandFill vertical
RpgTheme.Expand(control);              // both directions
RpgTheme.SetMinW(control, 200);        // set minimum width
RpgTheme.SetMinH(control, 100);        // set minimum height
RpgTheme.CreateSpacer(height, width);  // empty control for spacing
RpgTheme.CreateSeparator();            // HSeparator
RpgTheme.CreateFooterRow(spacing);     // separator + centered HBox row
RpgTheme.CreateCard(bgTexture, npMargins); // sub-panel with NinePatch
```

---

## 10. Creating a New Form (Step-by-Step)

```csharp
public partial class MyForm : RpgBaseForm
{
    public MyForm() : base("My Panel", new Vector2(400, 300), style: "v2")
    {
        CloseOnEscape = true;
        ZIndex = ZDialog;
    }

    protected override void BuildContent()
    {
        var col = RpgTheme.CreateColumn();
        ContentContainer.AddChild(col);

        // Add a section with title
        var section = RpgTheme.CreateSection("Stats");
        col.AddChild(section);

        // Add a stat row
        var row = RpgTheme.CreateRow();
        section.AddChild(row);
        row.AddChild(new Label { Text = "HP:" });
        row.AddChild(RpgTheme.ExpandH(new Label { Text = "100/100" }));

        // Add a scrollable list
        var scrollArea = RpgTheme.CreateScrollArea();
        col.AddChild(RpgTheme.ExpandV(scrollArea));
        var content = scrollArea.GetMeta("content").As<VBoxContainer>();
        // ... add items to content

        // Footer with buttons
        var footer = RpgTheme.CreateFooterRow();
        col.AddChild(footer);
        var footerRow = footer.GetMeta("row").As<HBoxContainer>();
        footerRow.AddChild(new Button { Text = "OK" });
    }
}
```

---

## 11. Asset Catalog

`RpgTheme.Assets` organizes all UI textures by category:

| Category | Count | Examples |
|----------|-------|---------|
| buttons | 14 | long_button.png, mid_button.png, mini_button.png |
| frames | 21 | info_window.png, inventory_frame.png, Thin_frame.png |
| bars | 12 | big_bar.png, basic_bar.png, skill_bar.png |
| lines | 15 | Hp_line.png, Mana_line.png, xp_line.png |
| portrait | 4 | Hero_icon_frame.png |
| silhouettes | 14 | warrior_silhouette_man.png, mage_silhouette_woman.png |
| icons | 17 | Bag.png, Fight.png, Quest.png, skills.png |
| slot_backgrounds | 21 | melee_background.png, helm_background.png |
| backgrounds | 4 | Paper_01.png, pattern.png, minimap.png |
| special_items | 4 | Book.png, Guitar.png |

Access: `RpgTheme.GetAsset("icons", "quest")` returns the `Texture2D`.

All assets live under `res://Data/UI/`.
