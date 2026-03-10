## Theme.gd — Color palette, styled button factories, and shared constants
class_name IndexerTheme
extends RefCounted

# ── Dark theme palette ─────────────────────────────────────────────
const BG_DARK      := Color(0.11, 0.11, 0.13)
const BG_PANEL     := Color(0.15, 0.15, 0.17)
const BG_HEADER    := Color(0.19, 0.19, 0.23)
const BG_INPUT     := Color(0.13, 0.13, 0.15)
const BG_HOVER     := Color(0.22, 0.22, 0.28)
const BG_SELECTED  := Color(0.25, 0.35, 0.55)
const BG_SECTION   := Color(0.14, 0.14, 0.16)

const TEXT_PRIMARY   := Color(0.90, 0.90, 0.92)
const TEXT_SECONDARY := Color(0.60, 0.60, 0.65)
const TEXT_MUTED     := Color(0.44, 0.44, 0.48)
const TEXT_ACCENT    := Color(0.40, 0.82, 1.0)
const TEXT_SUCCESS   := Color(0.40, 0.92, 0.50)
const TEXT_WARNING   := Color(0.95, 0.78, 0.25)
const TEXT_DANGER    := Color(1.0, 0.42, 0.42)

const ACCENT         := Color(0.35, 0.65, 1.0)
const ACCENT_HOVER   := Color(0.45, 0.72, 1.0)
const BORDER         := Color(0.24, 0.24, 0.28)

# Styled button backgrounds
const BG_BTN_PRIMARY   := Color(0.22, 0.48, 0.82)
const BG_BTN_PRIMARY_H := Color(0.28, 0.55, 0.90)
const BG_BTN_PRIMARY_P := Color(0.18, 0.40, 0.72)
const BG_BTN_SUCCESS   := Color(0.18, 0.52, 0.28)
const BG_BTN_SUCCESS_H := Color(0.22, 0.60, 0.34)
const BG_BTN_SUCCESS_P := Color(0.14, 0.44, 0.22)
const BG_BTN_DANGER    := Color(0.62, 0.18, 0.18)
const BG_BTN_DANGER_H  := Color(0.72, 0.24, 0.24)
const BG_BTN_DANGER_P  := Color(0.52, 0.14, 0.14)
const BG_BTN_GHOST     := Color(0.18, 0.18, 0.22)
const BG_BTN_GHOST_H   := Color(0.24, 0.24, 0.30)

# Tool toggle
const BG_TOOL_NORMAL   := Color(0.18, 0.18, 0.22)
const BG_TOOL_ACTIVE   := Color(0.28, 0.52, 0.82)

const FONT_SIZE_SM := 11
const FONT_SIZE_MD := 13
const FONT_SIZE_LG := 15
const FONT_SIZE_XL := 16

# ── StyleBox helpers ──────────────────────────────────────────────

static func _flat_box(bg: Color, corner := 4, margin_h := 8, margin_v := 4, border_col := Color.TRANSPARENT) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.set_corner_radius_all(corner)
	sb.content_margin_left = margin_h
	sb.content_margin_right = margin_h
	sb.content_margin_top = margin_v
	sb.content_margin_bottom = margin_v
	if border_col.a > 0.01:
		sb.border_color = border_col
		sb.set_border_width_all(1)
	return sb


# ── Widget factories ──────────────────────────────────────────────

static func label(text: String, color := TEXT_PRIMARY, size := FONT_SIZE_MD) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", color)
	l.add_theme_font_size_override("font_size", size)
	return l


static func heading(text: String) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", TEXT_ACCENT)
	l.add_theme_font_size_override("font_size", FONT_SIZE_LG)
	return l


static func section_label(text: String) -> Label:
	var l := Label.new()
	l.text = text.to_upper()
	l.add_theme_color_override("font_color", TEXT_MUTED)
	l.add_theme_font_size_override("font_size", FONT_SIZE_SM)
	return l


# ── Button factories ─────────────────────────────────────────────

## Plain text button (ghost style)
static func button(text: String, callback: Callable = Callable(), color := TEXT_PRIMARY) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_color_override("font_color", color)
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_GHOST, 3, 6, 3))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_GHOST_H, 3, 6, 3))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_BTN_GHOST, 3, 6, 3))
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


## Primary action button (blue background, white text)
static func primary_button(text: String, callback: Callable = Callable(), min_w := 0) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_color_override("font_color", Color.WHITE)
	b.add_theme_color_override("font_hover_color", Color.WHITE)
	b.add_theme_color_override("font_pressed_color", Color(0.85, 0.85, 0.85))
	b.add_theme_font_size_override("font_size", FONT_SIZE_LG)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_PRIMARY, 5, 12, 5))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_PRIMARY_H, 5, 12, 5))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_BTN_PRIMARY_P, 5, 12, 5))
	if min_w > 0:
		b.custom_minimum_size.x = min_w
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


## Success action button (green background)
static func success_button(text: String, callback: Callable = Callable(), min_w := 0) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_color_override("font_color", Color.WHITE)
	b.add_theme_color_override("font_hover_color", Color.WHITE)
	b.add_theme_color_override("font_pressed_color", Color(0.85, 0.85, 0.85))
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_SUCCESS, 5, 10, 4))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_SUCCESS_H, 5, 10, 4))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_BTN_SUCCESS_P, 5, 10, 4))
	if min_w > 0:
		b.custom_minimum_size.x = min_w
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


## Danger button (red background)
static func danger_button(text: String, callback: Callable = Callable()) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_color_override("font_color", Color.WHITE)
	b.add_theme_color_override("font_hover_color", Color.WHITE)
	b.add_theme_font_size_override("font_size", FONT_SIZE_SM)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_DANGER, 4, 8, 3))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_DANGER_H, 4, 8, 3))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_BTN_DANGER_P, 4, 8, 3))
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


## Small icon/text button
static func icon_button(text: String, callback: Callable = Callable(), tooltip := "", min_w := 32) -> Button:
	var b := Button.new()
	b.text = text
	b.tooltip_text = tooltip
	b.custom_minimum_size.x = min_w
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_GHOST, 3, 4, 2))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_GHOST_H, 3, 4, 2))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_BTN_GHOST, 3, 4, 2))
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


## Tool mode toggle button with active/inactive styling
static func tool_toggle(text: String, tooltip: String) -> Button:
	var b := Button.new()
	b.text = text
	b.tooltip_text = tooltip
	b.toggle_mode = true
	b.custom_minimum_size = Vector2(80, 30)
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	b.add_theme_color_override("font_color", TEXT_SECONDARY)
	b.add_theme_color_override("font_hover_color", TEXT_PRIMARY)
	b.add_theme_color_override("font_pressed_color", Color.WHITE)
	b.add_theme_stylebox_override("normal", _flat_box(BG_TOOL_NORMAL, 4, 8, 4, BORDER))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_GHOST_H, 4, 8, 4, BORDER))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_TOOL_ACTIVE, 4, 8, 4, Color(0.35, 0.60, 0.92, 0.6)))
	return b


## Preset button (small, compact)
static func preset_button(text: String, callback: Callable, tooltip := "") -> Button:
	var b := Button.new()
	b.text = text
	b.tooltip_text = tooltip
	b.add_theme_font_size_override("font_size", FONT_SIZE_SM)
	b.add_theme_color_override("font_color", TEXT_ACCENT)
	b.add_theme_stylebox_override("normal", _flat_box(BG_BTN_GHOST, 3, 6, 2, BORDER))
	b.add_theme_stylebox_override("hover", _flat_box(BG_BTN_GHOST_H, 3, 6, 2, ACCENT))
	b.add_theme_stylebox_override("pressed", _flat_box(BG_TOOL_ACTIVE, 3, 6, 2))
	if callback.is_valid():
		b.pressed.connect(callback)
	return b


# ── Layout helpers ───────────────────────────────────────────────

static func spinbox(min_val: float, max_val: float, value: float, callback: Callable = Callable()) -> SpinBox:
	var s := SpinBox.new()
	s.min_value = min_val
	s.max_value = max_val
	s.value = value
	s.custom_minimum_size.x = 50
	s.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	if callback.is_valid():
		s.value_changed.connect(callback)
	return s


static func separator_v() -> VSeparator:
	var s := VSeparator.new()
	s.custom_minimum_size.x = 8
	return s


static func separator_h() -> HSeparator:
	var s := HSeparator.new()
	s.add_theme_constant_override("separation", 4)
	return s


static func spacer() -> Control:
	var c := Control.new()
	c.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return c


static func styled_panel() -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = BG_PANEL
	sb.border_color = BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(3)
	sb.set_content_margin_all(4)
	return sb


## Section container with subtle background
static func section_box(content_margin := 6) -> PanelContainer:
	var pc := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = BG_SECTION
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(content_margin)
	pc.add_theme_stylebox_override("panel", sb)
	return pc
