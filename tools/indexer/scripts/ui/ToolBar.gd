## ToolBar.gd — Top toolbar: tool modes, snap, zoom, and primary actions
class_name IndexerToolBar
extends PanelContainer

signal tool_changed(mode: int)     # 0=Select, 1=Draw, 2=Pan
signal snap_changed(mode: int, sx: int, sy: int)
signal zoom_in_pressed
signal zoom_out_pressed
signal zoom_fit_pressed
signal zoom_reset_pressed
signal save_pressed
signal index_pressed

enum Tool { SELECT, DRAW, PAN }

var _tool_buttons: Array[Button] = []
var _snap_buttons: Array[Button] = []
var _snap_spin_row: HBoxContainer
var _spin_snap_x: SpinBox
var _spin_snap_y: SpinBox
var _current_tool: int = Tool.SELECT
var _current_snap: int = 0


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

	# ── Snap ──
	hbox.add_child(IndexerTheme.label("Snap:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	var snap_defs := [["Off", 0], ["Pot.2", 2], ["Sq", 3], ["Grid", 1]]
	for sd in snap_defs:
		var btn := IndexerTheme.preset_button(sd[0], Callable(), ["Sin snap", "Multiplo", "Potencia de 2", "Cuadrado P2"][sd[1]])
		btn.toggle_mode = true
		btn.custom_minimum_size.x = 42
		btn.pressed.connect(_on_snap_pressed.bind(sd[1]))
		hbox.add_child(btn)
		_snap_buttons.append(btn)
	_snap_buttons[0].button_pressed = true

	_snap_spin_row = HBoxContainer.new()
	_snap_spin_row.add_theme_constant_override("separation", 2)
	_snap_spin_row.visible = false
	hbox.add_child(_snap_spin_row)
	_snap_spin_row.add_child(IndexerTheme.label("X:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_snap_x = IndexerTheme.spinbox(1, 512, 32, func(_v): _emit_snap())
	_spin_snap_x.custom_minimum_size.x = 56
	_snap_spin_row.add_child(_spin_snap_x)
	_snap_spin_row.add_child(IndexerTheme.label("Y:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_snap_y = IndexerTheme.spinbox(1, 512, 32, func(_v): _emit_snap())
	_spin_snap_y.custom_minimum_size.x = 56
	_snap_spin_row.add_child(_spin_snap_y)

	hbox.add_child(IndexerTheme.separator_v())

	# ── Zoom ──
	hbox.add_child(IndexerTheme.icon_button("+", func(): zoom_in_pressed.emit(), "Zoom in", 28))
	hbox.add_child(IndexerTheme.icon_button("–", func(): zoom_out_pressed.emit(), "Zoom out", 28))
	hbox.add_child(IndexerTheme.icon_button("1:1", func(): zoom_reset_pressed.emit(), "Zoom 100%", 36))
	hbox.add_child(IndexerTheme.icon_button("Fit", func(): zoom_fit_pressed.emit(), "Ajustar al canvas", 36))

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


func set_snap(mode: int, sx: int = -1, sy: int = -1) -> void:
	_current_snap = mode
	for i in range(_snap_buttons.size()):
		_snap_buttons[i].set_pressed_no_signal(_snap_buttons[i] == _get_snap_btn(mode))
	_snap_spin_row.visible = (mode == 1)
	if sx > 0 and _spin_snap_x != null:
		_spin_snap_x.value = sx
	if sy > 0 and _spin_snap_y != null:
		_spin_snap_y.value = sy
	_emit_snap()


func _on_tool_pressed(mode: int) -> void:
	_current_tool = mode
	for i in range(_tool_buttons.size()):
		_tool_buttons[i].set_pressed_no_signal(i == mode)
	tool_changed.emit(mode)


func _on_snap_pressed(mode: int) -> void:
	_current_snap = mode
	for i in range(_snap_buttons.size()):
		_snap_buttons[i].set_pressed_no_signal(_snap_buttons[i] == _get_snap_btn(mode))
	_snap_spin_row.visible = (mode == 1)
	_emit_snap()


func _get_snap_btn(mode: int) -> Button:
	# snap_defs order: Off=0, Pot.2=2, Sq=3, Grid=1 → button indices 0,1,2,3
	var map := {0: 0, 2: 1, 3: 2, 1: 3}
	return _snap_buttons[map.get(mode, 0)]


func _emit_snap() -> void:
	snap_changed.emit(_current_snap, int(_spin_snap_x.value), int(_spin_snap_y.value))
