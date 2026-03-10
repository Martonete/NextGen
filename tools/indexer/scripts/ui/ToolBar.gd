## ToolBar.gd — Top toolbar: tool modes, snap, zoom, and primary actions
class_name IndexerToolBar
extends PanelContainer

signal tool_changed(mode: int)     # 0=Select, 1=Draw, 2=Pan
signal snap_changed(mode: int)
signal zoom_in_pressed
signal zoom_out_pressed
signal zoom_fit_pressed
signal zoom_reset_pressed
signal save_pressed
signal index_pressed
signal grid_toggled(visible: bool)
signal grid_cell_changed(cell_w: int, cell_h: int)

enum Tool { SELECT, DRAW, PAN }

var _tool_buttons: Array[Button] = []
var _snap_buttons: Array[Button] = []
var _current_tool: int = Tool.SELECT
var _current_snap: int = 0
var _chk_grid: CheckBox
var _opt_grid_cell: OptionButton
var _grid_custom_row: HBoxContainer
var _spin_grid_w: SpinBox
var _spin_grid_h: SpinBox


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
		["Seleccionar", "Seleccionar / mover frames (V)", Tool.SELECT],
		["Dibujar", "Dibujar nuevo frame (R/D)", Tool.DRAW],
		["Mover", "Mover canvas / pan (H)", Tool.PAN],
	]
	for t in tools:
		var btn := IndexerTheme.tool_toggle(t[0], t[1])
		btn.pressed.connect(_on_tool_pressed.bind(t[2]))
		hbox.add_child(btn)
		_tool_buttons.append(btn)
	_tool_buttons[0].button_pressed = true

	hbox.add_child(IndexerTheme.separator_v())

	# ── Smart detection ──
	var snap_defs := [["Smart", 4], ["Off", 0]]
	var snap_tips := {4: "Detección inteligente de sprites + snap a grilla", 0: "Sin detección"}
	for sd in snap_defs:
		var btn := IndexerTheme.preset_button(sd[0], Callable(), snap_tips.get(sd[1], ""))
		btn.toggle_mode = true
		btn.custom_minimum_size.x = 50
		btn.pressed.connect(_on_snap_pressed.bind(sd[1]))
		hbox.add_child(btn)
		_snap_buttons.append(btn)
	_snap_buttons[0].button_pressed = true

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
	_chk_grid.add_theme_color_override("font_color", IndexerTheme.TEXT_NORMAL)
	_chk_grid.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_chk_grid.tooltip_text = "Mostrar/ocultar grilla visual (también snap al dibujar)"
	_chk_grid.toggled.connect(func(on: bool): grid_toggled.emit(on))
	hbox.add_child(_chk_grid)

	_opt_grid_cell = OptionButton.new()
	_opt_grid_cell.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_opt_grid_cell.custom_minimum_size.x = 100
	_opt_grid_cell.tooltip_text = "Tamaño de celda de la grilla"
	_opt_grid_cell.add_item("128x128", 0)
	_opt_grid_cell.add_item("128x64", 1)
	_opt_grid_cell.add_item("64x64", 2)
	_opt_grid_cell.add_item("64x32", 3)
	_opt_grid_cell.add_item("32x32", 4)
	_opt_grid_cell.add_item("Custom...", 5)
	_opt_grid_cell.selected = 0
	_opt_grid_cell.item_selected.connect(_on_grid_cell_selected)
	hbox.add_child(_opt_grid_cell)

	# Custom grid spinboxes (hidden by default)
	_grid_custom_row = HBoxContainer.new()
	_grid_custom_row.add_theme_constant_override("separation", 2)
	_grid_custom_row.visible = false
	hbox.add_child(_grid_custom_row)
	_grid_custom_row.add_child(IndexerTheme.label("W:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_grid_w = IndexerTheme.spinbox(8, 512, 128, func(_v): _emit_grid_cell())
	_spin_grid_w.custom_minimum_size.x = 56
	_grid_custom_row.add_child(_spin_grid_w)
	_grid_custom_row.add_child(IndexerTheme.label("H:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_grid_h = IndexerTheme.spinbox(8, 512, 128, func(_v): _emit_grid_cell())
	_spin_grid_h.custom_minimum_size.x = 56
	_grid_custom_row.add_child(_spin_grid_h)

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


func set_snap(mode: int) -> void:
	_current_snap = mode
	for i in range(_snap_buttons.size()):
		_snap_buttons[i].set_pressed_no_signal(_snap_buttons[i] == _get_snap_btn(mode))
	snap_changed.emit(mode)


func set_grid(visible: bool, cell_w: int = -1, cell_h: int = -1) -> void:
	_chk_grid.set_pressed_no_signal(visible)
	if cell_w > 0 and cell_h > 0:
		_select_grid_preset(cell_w, cell_h)


func _on_tool_pressed(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].set_pressed_no_signal(i == mode)
	tool_changed.emit(mode)


func _on_snap_pressed(mode: int) -> void:
	_current_snap = mode
	for i in range(_snap_buttons.size()):
		_snap_buttons[i].set_pressed_no_signal(_snap_buttons[i] == _get_snap_btn(mode))
	snap_changed.emit(mode)


func _get_snap_btn(mode: int) -> Button:
	# snap_defs order: Smart=4, Off=0 → button indices 0, 1
	var map := {4: 0, 0: 1}
	return _snap_buttons[map.get(mode, 0)]


# ── Grid cell presets ────────────────────────────────────────────────────────
# Preset index → [cell_w, cell_h]
const GRID_PRESETS := [[128, 128], [128, 64], [64, 64], [64, 32], [32, 32]]

func _on_grid_cell_selected(idx: int) -> void:
	if idx < GRID_PRESETS.size():
		_grid_custom_row.visible = false
		var preset: Array = GRID_PRESETS[idx]
		grid_cell_changed.emit(preset[0], preset[1])
	else:
		# Custom
		_grid_custom_row.visible = true
		_emit_grid_cell()


func _emit_grid_cell() -> void:
	grid_cell_changed.emit(int(_spin_grid_w.value), int(_spin_grid_h.value))


func _select_grid_preset(cw: int, ch: int) -> void:
	for i in range(GRID_PRESETS.size()):
		var p: Array = GRID_PRESETS[i]
		if p[0] == cw and p[1] == ch:
			_opt_grid_cell.selected = i
			_grid_custom_row.visible = false
			return
	# Not a preset → Custom
	_opt_grid_cell.selected = GRID_PRESETS.size()  # Custom item
	_grid_custom_row.visible = true
	_spin_grid_w.value = cw
	_spin_grid_h.value = ch
