## Theme.gd — Color palette and shared styling constants
class_name IndexerTheme
extends RefCounted

# ── Dark theme palette ─────────────────────────────────────────────
const BG_DARK      := Color(0.12, 0.12, 0.14)
const BG_PANEL     := Color(0.16, 0.16, 0.18)
const BG_HEADER    := Color(0.20, 0.20, 0.24)
const BG_INPUT     := Color(0.13, 0.13, 0.15)
const BG_HOVER     := Color(0.22, 0.22, 0.28)
const BG_SELECTED  := Color(0.25, 0.35, 0.55)

const TEXT_PRIMARY   := Color(0.88, 0.88, 0.90)
const TEXT_SECONDARY := Color(0.58, 0.58, 0.62)
const TEXT_MUTED     := Color(0.42, 0.42, 0.46)
const TEXT_ACCENT    := Color(0.40, 0.82, 1.0)
const TEXT_SUCCESS   := Color(0.40, 0.90, 0.50)
const TEXT_WARNING   := Color(0.95, 0.75, 0.25)
const TEXT_DANGER    := Color(1.0, 0.40, 0.40)

const ACCENT         := Color(0.35, 0.65, 1.0)
const ACCENT_HOVER   := Color(0.45, 0.72, 1.0)
const BORDER         := Color(0.25, 0.25, 0.30)

const FONT_SIZE_SM := 10
const FONT_SIZE_MD := 12
const FONT_SIZE_LG := 14

# ── Widget factories ───────────────────────────────────────────────

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

static func button(text: String, callback: Callable = Callable(), color := TEXT_PRIMARY) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_color_override("font_color", color)
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	if callback.is_valid():
		b.pressed.connect(callback)
	return b

static func icon_button(text: String, callback: Callable = Callable(), tooltip := "", min_w := 32) -> Button:
	var b := Button.new()
	b.text = text
	b.tooltip_text = tooltip
	b.custom_minimum_size.x = min_w
	b.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	if callback.is_valid():
		b.pressed.connect(callback)
	return b

static func tool_button(text: String, tooltip: String, toggle := false) -> Button:
	var b := Button.new()
	b.text = text
	b.tooltip_text = tooltip
	b.toggle_mode = toggle
	b.custom_minimum_size = Vector2(36, 28)
	b.add_theme_font_size_override("font_size", 15)
	return b

static func spinbox(min_val: float, max_val: float, value: float, callback: Callable = Callable()) -> SpinBox:
	var s := SpinBox.new()
	s.min_value = min_val
	s.max_value = max_val
	s.value = value
	s.custom_minimum_size.x = 70
	s.add_theme_font_size_override("font_size", FONT_SIZE_MD)
	if callback.is_valid():
		s.value_changed.connect(callback)
	return s

static func separator_v() -> VSeparator:
	var s := VSeparator.new()
	s.custom_minimum_size.x = 8
	return s

static func separator_h() -> HSeparator:
	return HSeparator.new()

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
