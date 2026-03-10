## ToolBar.gd — Top toolbar: tool modes, snap, zoom, and primary actions
class_name IndexerToolBar
extends PanelContainer

signal tool_changed(mode: int)     # 0=Select+Draw, 1=Pan
signal detect_toggled(enabled: bool)
signal zoom_in_pressed
signal zoom_out_pressed
signal zoom_fit_pressed
signal zoom_reset_pressed
signal save_pressed
signal index_pressed
signal grid_toggled(visible: bool)
signal grid_config_changed(cell_w: int, cell_h: int, line_w: float, col: Color)

var _tool_buttons: Array[Button] = []
var _current_tool: int = 0
var _chk_detect: CheckBox
var _chk_grid: CheckBox
var _btn_grid_config: MenuButton
# Grid config state
var _grid_cell_w: int = 128
var _grid_cell_h: int = 128
var _grid_line_w: float = 1.0
var _grid_color: Color = Color(1.0, 0.85, 0.0)
# Config popup
var _config_popup: PopupMenu
var _size_sub: PopupMenu
var _border_sub: PopupMenu
var _color_picker_window: Window
var _color_picker: ColorPicker


func _ready() -> void:
	# Toolbar background
	var bg := StyleBoxFlat.new()
	bg.bg_color = IndexerTheme.BG_HEADER
	bg.content_margin_left = 6
	bg.content_margin_right = 6
	bg.content_margin_top = 4
	bg.content_margin_bottom = 4
	add_theme_stylebox_override("panel", bg)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 6)
	add_child(hbox)

	# ── Tool modes ──
	var tools := [
		["Editar", "Seleccionar / dibujar / mover frames", 0],
		["Mover", "Mover canvas / pan (H)", 1],
	]
	for t in tools:
		var btn := IndexerTheme.tool_toggle(t[0], t[1])
		btn.pressed.connect(_on_tool_pressed.bind(t[2]))
		hbox.add_child(btn)
		_tool_buttons.append(btn)
	_tool_buttons[0].button_pressed = true

	hbox.add_child(IndexerTheme.separator_v())

	# ── Frame detection ──
	_chk_detect = CheckBox.new()
	_chk_detect.text = "Frame detect"
	_chk_detect.button_pressed = true
	_chk_detect.add_theme_color_override("font_color", Color(0.85, 0.85, 0.88))
	_chk_detect.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_chk_detect.tooltip_text = "Detectar sprites al pasar el mouse"
	_chk_detect.toggled.connect(func(on: bool): detect_toggled.emit(on))
	hbox.add_child(_chk_detect)

	hbox.add_child(IndexerTheme.separator_v())

	# ── Zoom ──
	hbox.add_child(IndexerTheme.icon_button("+", func(): zoom_in_pressed.emit(), "Zoom in", 28))
	hbox.add_child(IndexerTheme.icon_button("–", func(): zoom_out_pressed.emit(), "Zoom out", 28))
	hbox.add_child(IndexerTheme.icon_button("1:1", func(): zoom_reset_pressed.emit(), "Zoom 100%", 36))
	hbox.add_child(IndexerTheme.icon_button("Fit", func(): zoom_fit_pressed.emit(), "Ajustar al canvas", 36))

	hbox.add_child(IndexerTheme.separator_v())

	# ── Grid overlay ──
	_chk_grid = CheckBox.new()
	_chk_grid.text = "Grid"
	_chk_grid.add_theme_color_override("font_color", Color(0.85, 0.85, 0.88))
	_chk_grid.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_chk_grid.tooltip_text = "Mostrar/ocultar grilla visual (también snap al dibujar)"
	_chk_grid.toggled.connect(func(on: bool): grid_toggled.emit(on))
	hbox.add_child(_chk_grid)

	_btn_grid_config = MenuButton.new()
	_btn_grid_config.text = "Config"
	_btn_grid_config.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_btn_grid_config.tooltip_text = "Configurar grilla: tamaño, borde, color"
	hbox.add_child(_btn_grid_config)
	_build_grid_config_menu()

	# ── Spacer ──
	hbox.add_child(IndexerTheme.spacer())

	# ── Primary actions ──
	var btn_index := IndexerTheme.primary_button("INDEXAR", func(): index_pressed.emit(), 90)
	hbox.add_child(btn_index)

	var btn_save := IndexerTheme.success_button("Guardar", func(): save_pressed.emit(), 80)
	hbox.add_child(btn_save)


func set_tool(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].button_pressed = (i == mode)
	tool_changed.emit(mode)


func set_detect(enabled: bool) -> void:
	_chk_detect.set_pressed_no_signal(enabled)


func set_grid(visible: bool, cell_w: int = -1, cell_h: int = -1, line_w: float = -1.0, col: Color = Color(-1, 0, 0)) -> void:
	_chk_grid.set_pressed_no_signal(visible)
	if cell_w > 0:
		_grid_cell_w = cell_w
	if cell_h > 0:
		_grid_cell_h = cell_h
	if line_w > 0:
		_grid_line_w = line_w
	if col.r >= 0:
		_grid_color = col
	_rebuild_config_checks()


func _on_tool_pressed(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].set_pressed_no_signal(i == mode)
	tool_changed.emit(mode)


# ── Grid config menu ─────────────────────────────────────────────────────────

const SIZE_PRESETS := [[512, 512], [256, 256], [256, 128], [128, 128], [128, 64], [64, 64], [64, 32], [32, 32]]
const SIZE_LABELS := ["512x512", "256x256", "256x128", "128x128", "128x64", "64x64", "64x32", "32x32"]
const BORDER_OPTIONS := [1, 2, 3, 4]

func _build_grid_config_menu() -> void:
	_config_popup = _btn_grid_config.get_popup()
	_config_popup.clear()

	# Size submenu
	_size_sub = PopupMenu.new()
	_size_sub.name = "SizeSubmenu"
	for i in range(SIZE_LABELS.size()):
		_size_sub.add_radio_check_item(SIZE_LABELS[i], i)
	_size_sub.set_item_checked(0, true)
	_size_sub.id_pressed.connect(_on_size_selected)
	_config_popup.add_child(_size_sub)
	_config_popup.add_submenu_item("Tamaño", "SizeSubmenu")

	# Border submenu
	_border_sub = PopupMenu.new()
	_border_sub.name = "BorderSubmenu"
	for i in range(BORDER_OPTIONS.size()):
		_border_sub.add_radio_check_item("%d px" % BORDER_OPTIONS[i], i)
	_border_sub.set_item_checked(0, true)
	_border_sub.id_pressed.connect(_on_border_selected)
	_config_popup.add_child(_border_sub)
	_config_popup.add_submenu_item("Borde", "BorderSubmenu")

	# Color item
	_config_popup.add_item("Color...", 100)
	_config_popup.id_pressed.connect(_on_config_item)

	# Color picker window (lazy, hidden)
	_color_picker_window = Window.new()
	_color_picker_window.title = "Color de grilla"
	_color_picker_window.size = Vector2i(320, 340)
	_color_picker_window.exclusive = true
	_color_picker_window.wrap_controls = true
	_color_picker_window.visible = false
	_color_picker_window.close_requested.connect(func(): _color_picker_window.hide())
	_color_picker = ColorPicker.new()
	_color_picker.color = _grid_color
	_color_picker.edit_alpha = false
	_color_picker.set_anchors_preset(Control.PRESET_FULL_RECT)
	_color_picker.color_changed.connect(_on_color_changed)
	_color_picker_window.add_child(_color_picker)
	add_child(_color_picker_window)


func _on_size_selected(id: int) -> void:
	if id < SIZE_PRESETS.size():
		var p: Array = SIZE_PRESETS[id]
		_grid_cell_w = p[0]
		_grid_cell_h = p[1]
		_rebuild_config_checks()
		_emit_grid_config()


func _on_border_selected(id: int) -> void:
	if id < BORDER_OPTIONS.size():
		_grid_line_w = float(BORDER_OPTIONS[id])
		_rebuild_config_checks()
		_emit_grid_config()


func _on_config_item(id: int) -> void:
	if id == 100:
		_color_picker.color = _grid_color
		_color_picker_window.popup_centered()


func _on_color_changed(col: Color) -> void:
	_grid_color = col
	_emit_grid_config()


func _emit_grid_config() -> void:
	grid_config_changed.emit(_grid_cell_w, _grid_cell_h, _grid_line_w, _grid_color)


func _rebuild_config_checks() -> void:
	if _size_sub == null:
		return
	for i in range(SIZE_PRESETS.size()):
		var p: Array = SIZE_PRESETS[i]
		_size_sub.set_item_checked(i, p[0] == _grid_cell_w and p[1] == _grid_cell_h)
	for i in range(BORDER_OPTIONS.size()):
		_border_sub.set_item_checked(i, BORDER_OPTIONS[i] == int(_grid_line_w))
