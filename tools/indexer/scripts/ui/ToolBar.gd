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
signal revert_pressed
signal grid_toggled(visible: bool)
signal grid_config_changed(cell_w: int, cell_h: int, line_w: float, col: Color)
signal frames_toggled(visible: bool)
signal frames_config_changed(bg_color: Color, bg_alpha: float, border_color: Color, border_width: float)
signal textures_toggled(visible: bool)

var _tool_buttons: Array[Button] = []
var _current_tool: int = 0

# Grid config
var _btn_grid_config: MenuButton
var _grid_cell_w: int = 128
var _grid_cell_h: int = 128
var _grid_line_w: float = 1.0
var _grid_color: Color = Color(1.0, 0.85, 0.0)
var _config_popup: PopupMenu
var _size_sub: PopupMenu
var _border_sub: PopupMenu
var _color_picker_window: Window
var _color_picker: ColorPicker
var _grid_visible: bool = false

# Frames config
var _btn_frames_config: MenuButton
var _frames_popup: PopupMenu
var _frames_border_sub: PopupMenu
var _frames_visible: bool = true
var _frames_bg_color: Color = Color(0.2, 0.5, 0.9)
var _frames_bg_alpha: float = 0.15
var _frames_border_color: Color = Color(0.3, 0.6, 1.0)
var _frames_border_width: float = 1.0
var _frames_detect: bool = false
var _textures_visible: bool = false
var _frames_bg_picker_window: Window
var _frames_bg_picker: ColorPicker
var _frames_border_picker_window: Window
var _frames_border_picker: ColorPicker


func _ready() -> void:
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

	# ── Zoom ──
	hbox.add_child(IndexerTheme.icon_button("+", func(): zoom_in_pressed.emit(), "Zoom in", 28))
	hbox.add_child(IndexerTheme.icon_button("–", func(): zoom_out_pressed.emit(), "Zoom out", 28))
	hbox.add_child(IndexerTheme.icon_button("1:1", func(): zoom_reset_pressed.emit(), "Zoom 100%", 36))
	hbox.add_child(IndexerTheme.icon_button("Fit", func(): zoom_fit_pressed.emit(), "Ajustar al canvas", 36))

	hbox.add_child(IndexerTheme.separator_v())

	# ── Grid overlay ──
	_btn_grid_config = MenuButton.new()
	_btn_grid_config.text = "Grid ▾"
	_btn_grid_config.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_btn_grid_config.add_theme_color_override("font_color", Color(0.85, 0.85, 0.88))
	_btn_grid_config.tooltip_text = "Grilla: mostrar/ocultar + configurar"
	hbox.add_child(_btn_grid_config)
	_build_grid_config_menu()

	# ── Frames overlay ──
	_btn_frames_config = MenuButton.new()
	_btn_frames_config.text = "Frames ▾"
	_btn_frames_config.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_btn_frames_config.add_theme_color_override("font_color", Color(0.85, 0.85, 0.88))
	_btn_frames_config.tooltip_text = "Frames: visibilidad, colores, detección"
	hbox.add_child(_btn_frames_config)
	_build_frames_config_menu()

	# ── Spacer ──
	hbox.add_child(IndexerTheme.spacer())

	# ── Primary actions ──
	var btn_revert := IndexerTheme.danger_button("DESHACER TODO", func(): revert_pressed.emit())
	hbox.add_child(btn_revert)
	var btn_save := IndexerTheme.success_button("GUARDAR", func(): save_pressed.emit(), 100)
	hbox.add_child(btn_save)


func set_tool(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].button_pressed = (i == mode)
	tool_changed.emit(mode)


func set_textures_visible(visible: bool) -> void:
	_textures_visible = visible
	if _frames_popup != null:
		var idx := _frames_popup.get_item_index(400)
		if idx >= 0:
			_frames_popup.set_item_checked(idx, visible)


func set_detect(enabled: bool) -> void:
	_frames_detect = enabled
	if _frames_popup != null:
		var idx := _frames_popup.get_item_index(300)
		if idx >= 0:
			_frames_popup.set_item_checked(idx, enabled)


func set_frames_visible(visible: bool) -> void:
	_frames_visible = visible
	if _frames_popup != null:
		var idx := _frames_popup.get_item_index(200)
		if idx >= 0:
			_frames_popup.set_item_checked(idx, visible)


func set_frames_config(bg_col: Color, bg_alpha: float, brd_col: Color, brd_w: float) -> void:
	_frames_bg_color = bg_col
	_frames_bg_alpha = bg_alpha
	_frames_border_color = brd_col
	_frames_border_width = brd_w
	_rebuild_frames_bg_checks()
	_rebuild_frames_border_checks()


func set_grid(visible: bool, cell_w: int = -1, cell_h: int = -1, line_w: float = -1.0, col: Color = Color(-1, 0, 0)) -> void:
	_grid_visible = visible
	if _config_popup != null:
		var item_idx := _config_popup.get_item_index(200)
		if item_idx >= 0:
			_config_popup.set_item_checked(item_idx, visible)
	if cell_w > 0:
		_grid_cell_w = cell_w
	if cell_h > 0:
		_grid_cell_h = cell_h
	if line_w > 0:
		_grid_line_w = line_w
	if col.r >= 0:
		_grid_color = col
	_rebuild_grid_checks()


func _on_tool_pressed(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].set_pressed_no_signal(i == mode)
	tool_changed.emit(mode)


# ══════════════════════════════════════════════════════════════════════════════
# Grid config menu
# ══════════════════════════════════════════════════════════════════════════════

const SIZE_PRESETS := [[512, 512], [256, 256], [256, 128], [128, 128], [128, 64], [64, 64], [64, 32], [32, 32]]
const SIZE_LABELS := ["512x512", "256x256", "256x128", "128x128", "128x64", "64x64", "64x32", "32x32"]
const BORDER_OPTIONS := [1, 2, 3, 4]

func _build_grid_config_menu() -> void:
	_config_popup = _btn_grid_config.get_popup()
	_config_popup.clear()

	_config_popup.add_check_item("Mostrar Grid", 200)
	_config_popup.set_item_checked(_config_popup.get_item_index(200), false)
	_config_popup.add_separator()

	_size_sub = PopupMenu.new()
	_size_sub.name = "SizeSubmenu"
	for i in range(SIZE_LABELS.size()):
		_size_sub.add_radio_check_item(SIZE_LABELS[i], i)
	_size_sub.set_item_checked(0, true)
	_size_sub.id_pressed.connect(_on_grid_size_selected)
	_config_popup.add_child(_size_sub)
	_config_popup.add_submenu_item("Tamaño", "SizeSubmenu")

	_border_sub = PopupMenu.new()
	_border_sub.name = "BorderSubmenu"
	for i in range(BORDER_OPTIONS.size()):
		_border_sub.add_radio_check_item("%d px" % BORDER_OPTIONS[i], i)
	_border_sub.set_item_checked(0, true)
	_border_sub.id_pressed.connect(_on_grid_border_selected)
	_config_popup.add_child(_border_sub)
	_config_popup.add_submenu_item("Borde", "BorderSubmenu")

	_config_popup.add_item("Color...", 100)
	_config_popup.id_pressed.connect(_on_grid_config_item)

	# Color picker (lazy add_child)
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
	_color_picker.color_changed.connect(func(c: Color): _grid_color = c; _emit_grid_config())
	_color_picker_window.add_child(_color_picker)


func _on_grid_size_selected(id: int) -> void:
	if id < SIZE_PRESETS.size():
		var p: Array = SIZE_PRESETS[id]
		_grid_cell_w = p[0]
		_grid_cell_h = p[1]
		_rebuild_grid_checks()
		_emit_grid_config()


func _on_grid_border_selected(id: int) -> void:
	if id < BORDER_OPTIONS.size():
		_grid_line_w = float(BORDER_OPTIONS[id])
		_rebuild_grid_checks()
		_emit_grid_config()


func _on_grid_config_item(id: int) -> void:
	if id == 200:
		_grid_visible = not _grid_visible
		var item_idx := _config_popup.get_item_index(200)
		_config_popup.set_item_checked(item_idx, _grid_visible)
		grid_toggled.emit(_grid_visible)
	elif id == 100:
		_color_picker.color = _grid_color
		if not _color_picker_window.is_inside_tree():
			add_child(_color_picker_window)
		_color_picker_window.popup_centered()


func _emit_grid_config() -> void:
	grid_config_changed.emit(_grid_cell_w, _grid_cell_h, _grid_line_w, _grid_color)


func _rebuild_grid_checks() -> void:
	if _size_sub == null:
		return
	for i in range(SIZE_PRESETS.size()):
		var p: Array = SIZE_PRESETS[i]
		_size_sub.set_item_checked(i, p[0] == _grid_cell_w and p[1] == _grid_cell_h)
	for i in range(BORDER_OPTIONS.size()):
		_border_sub.set_item_checked(i, BORDER_OPTIONS[i] == int(_grid_line_w))


# ══════════════════════════════════════════════════════════════════════════════
# Frames config menu
# ══════════════════════════════════════════════════════════════════════════════

const FRAME_BORDER_OPTIONS := [1, 2, 3, 4]

func _build_frames_config_menu() -> void:
	_frames_popup = _btn_frames_config.get_popup()
	_frames_popup.clear()

	# Toggle visibility
	_frames_popup.add_check_item("Ver frames", 200)
	_frames_popup.set_item_checked(_frames_popup.get_item_index(200), _frames_visible)
	_frames_popup.add_separator()

	# Background submenu
	var bg_sub := PopupMenu.new()
	bg_sub.name = "FrameBgSubmenu"
	bg_sub.add_item("Color...", 0)
	bg_sub.add_separator()
	bg_sub.add_radio_check_item("Opacidad 5%", 10)
	bg_sub.add_radio_check_item("Opacidad 10%", 11)
	bg_sub.add_radio_check_item("Opacidad 15%", 12)
	bg_sub.add_radio_check_item("Opacidad 20%", 13)
	bg_sub.add_radio_check_item("Opacidad 30%", 14)
	bg_sub.add_radio_check_item("Opacidad 50%", 15)
	bg_sub.set_item_checked(bg_sub.get_item_index(12), true)  # 15% default
	bg_sub.id_pressed.connect(_on_frames_bg_item)
	_frames_popup.add_child(bg_sub)
	_frames_popup.add_submenu_item("Background", "FrameBgSubmenu")

	# Border submenu
	var border_sub := PopupMenu.new()
	border_sub.name = "FrameBorderSubmenu"
	border_sub.add_item("Color...", 0)
	border_sub.add_separator()
	for i in range(FRAME_BORDER_OPTIONS.size()):
		border_sub.add_radio_check_item("%d px" % FRAME_BORDER_OPTIONS[i], 10 + i)
	border_sub.set_item_checked(border_sub.get_item_index(10), true)  # 1px default
	border_sub.id_pressed.connect(_on_frames_border_item)
	_frames_popup.add_child(border_sub)
	_frames_popup.add_submenu_item("Borde", "FrameBorderSubmenu")
	_frames_border_sub = border_sub

	_frames_popup.add_separator()

	# Smart detect
	_frames_popup.add_check_item("Smart frame detection", 300)
	_frames_popup.set_item_checked(_frames_popup.get_item_index(300), _frames_detect)

	# Texture overlay detection
	_frames_popup.add_check_item("Detectar texturas (indices)", 400)
	_frames_popup.set_item_checked(_frames_popup.get_item_index(400), _textures_visible)

	_frames_popup.id_pressed.connect(_on_frames_config_item)

	# Color pickers (lazy add_child)
	_frames_bg_picker_window = Window.new()
	_frames_bg_picker_window.title = "Color de fondo de frames"
	_frames_bg_picker_window.size = Vector2i(320, 340)
	_frames_bg_picker_window.exclusive = true
	_frames_bg_picker_window.wrap_controls = true
	_frames_bg_picker_window.visible = false
	_frames_bg_picker_window.close_requested.connect(func(): _frames_bg_picker_window.hide())
	_frames_bg_picker = ColorPicker.new()
	_frames_bg_picker.color = _frames_bg_color
	_frames_bg_picker.edit_alpha = false
	_frames_bg_picker.set_anchors_preset(Control.PRESET_FULL_RECT)
	_frames_bg_picker.color_changed.connect(func(c: Color): _frames_bg_color = c; _emit_frames_config())
	_frames_bg_picker_window.add_child(_frames_bg_picker)

	_frames_border_picker_window = Window.new()
	_frames_border_picker_window.title = "Color de borde de frames"
	_frames_border_picker_window.size = Vector2i(320, 340)
	_frames_border_picker_window.exclusive = true
	_frames_border_picker_window.wrap_controls = true
	_frames_border_picker_window.visible = false
	_frames_border_picker_window.close_requested.connect(func(): _frames_border_picker_window.hide())
	_frames_border_picker = ColorPicker.new()
	_frames_border_picker.color = _frames_border_color
	_frames_border_picker.edit_alpha = false
	_frames_border_picker.set_anchors_preset(Control.PRESET_FULL_RECT)
	_frames_border_picker.color_changed.connect(func(c: Color): _frames_border_color = c; _emit_frames_config())
	_frames_border_picker_window.add_child(_frames_border_picker)


func _on_frames_config_item(id: int) -> void:
	if id == 200:
		_frames_visible = not _frames_visible
		var idx := _frames_popup.get_item_index(200)
		_frames_popup.set_item_checked(idx, _frames_visible)
		frames_toggled.emit(_frames_visible)
	elif id == 300:
		_frames_detect = not _frames_detect
		var idx := _frames_popup.get_item_index(300)
		_frames_popup.set_item_checked(idx, _frames_detect)
		detect_toggled.emit(_frames_detect)
	elif id == 400:
		_textures_visible = not _textures_visible
		var idx := _frames_popup.get_item_index(400)
		_frames_popup.set_item_checked(idx, _textures_visible)
		textures_toggled.emit(_textures_visible)


func _on_frames_bg_item(id: int) -> void:
	if id == 0:
		_frames_bg_picker.color = _frames_bg_color
		if not _frames_bg_picker_window.is_inside_tree():
			add_child(_frames_bg_picker_window)
		_frames_bg_picker_window.popup_centered()
	else:
		var alpha_map := {10: 0.05, 11: 0.1, 12: 0.15, 13: 0.2, 14: 0.3, 15: 0.5}
		if alpha_map.has(id):
			_frames_bg_alpha = alpha_map[id]
			_rebuild_frames_bg_checks()
			_emit_frames_config()


func _on_frames_border_item(id: int) -> void:
	if id == 0:
		_frames_border_picker.color = _frames_border_color
		if not _frames_border_picker_window.is_inside_tree():
			add_child(_frames_border_picker_window)
		_frames_border_picker_window.popup_centered()
	elif id >= 10 and id < 10 + FRAME_BORDER_OPTIONS.size():
		_frames_border_width = float(FRAME_BORDER_OPTIONS[id - 10])
		_rebuild_frames_border_checks()
		_emit_frames_config()


func _emit_frames_config() -> void:
	frames_config_changed.emit(_frames_bg_color, _frames_bg_alpha, _frames_border_color, _frames_border_width)


func _rebuild_frames_bg_checks() -> void:
	var bg_sub: PopupMenu = _frames_popup.get_node_or_null("FrameBgSubmenu")
	if bg_sub == null:
		return
	var alpha_map := {10: 0.05, 11: 0.1, 12: 0.15, 13: 0.2, 14: 0.3, 15: 0.5}
	for item_id in alpha_map:
		var idx := bg_sub.get_item_index(item_id)
		if idx >= 0:
			bg_sub.set_item_checked(idx, absf(_frames_bg_alpha - alpha_map[item_id]) < 0.001)


func _rebuild_frames_border_checks() -> void:
	if _frames_border_sub == null:
		return
	for i in range(FRAME_BORDER_OPTIONS.size()):
		var idx := _frames_border_sub.get_item_index(10 + i)
		if idx >= 0:
			_frames_border_sub.set_item_checked(idx, FRAME_BORDER_OPTIONS[i] == int(_frames_border_width))
