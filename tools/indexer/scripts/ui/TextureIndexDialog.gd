## TextureIndexDialog.gd — Formulario para indexar una textura multi-tile
class_name TextureIndexDialog
extends Window

signal confirmed(name: String, category: String, capa: int)
signal split_requested(frame: Dictionary, tiles_w: int, tiles_h: int)

var _split_done: bool = false
var _spin_w: SpinBox
var _spin_h: SpinBox
var _input_name: LineEdit
var _opt_type: OptionButton
var _spin_capa: SpinBox
var _preview: TextureRect
var _lbl_summary: Label
var _lbl_warn: Label
var _btn_split: Button
var _btn_save: Button
var _lbl_save_hint: Label

# Source frame info (set before popup)
var _source_frame: Dictionary = {}
var _source_texture: ImageTexture = null
var _tiles_w: int = 0
var _tiles_h: int = 0


func _ready() -> void:
	title = "Indexar Textura"
	size = Vector2i(440, 560)
	exclusive = true
	wrap_controls = true
	visible = false
	close_requested.connect(func(): hide())

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	add_child(margin)

	var root := VBoxContainer.new()
	root.add_theme_constant_override("separation", 8)
	margin.add_child(root)

	# Name
	var name_row := HBoxContainer.new()
	name_row.add_theme_constant_override("separation", 6)
	root.add_child(name_row)
	name_row.add_child(_label("Nombre:"))
	_input_name = LineEdit.new()
	_input_name.placeholder_text = "Ej: (PRD) Terreno Nuevo"
	_input_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_input_name.add_theme_font_size_override("font_size", 13)
	_input_name.text_changed.connect(func(_t): _update_save_state())
	name_row.add_child(_input_name)

	# Tile size (editable)
	var size_row := HBoxContainer.new()
	size_row.add_theme_constant_override("separation", 6)
	root.add_child(size_row)
	size_row.add_child(_label("Tiles:"))
	_spin_w = SpinBox.new()
	_spin_w.min_value = 1
	_spin_w.max_value = 64
	_spin_w.value = 1
	_spin_w.suffix = " ancho"
	_spin_w.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_spin_w.value_changed.connect(func(_v): _on_tiles_changed())
	size_row.add_child(_spin_w)
	var lbl_x := Label.new()
	lbl_x.text = "×"
	lbl_x.add_theme_font_size_override("font_size", 14)
	size_row.add_child(lbl_x)
	_spin_h = SpinBox.new()
	_spin_h.min_value = 1
	_spin_h.max_value = 64
	_spin_h.value = 1
	_spin_h.suffix = " alto"
	_spin_h.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_spin_h.value_changed.connect(func(_v): _on_tiles_changed())
	size_row.add_child(_spin_h)

	# Category dropdown
	var type_row := HBoxContainer.new()
	type_row.add_theme_constant_override("separation", 6)
	root.add_child(type_row)
	type_row.add_child(_label("Categoría:"))
	_opt_type = OptionButton.new()
	_opt_type.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_opt_type.add_theme_font_size_override("font_size", 13)
	type_row.add_child(_opt_type)

	# Layer
	var capa_row := HBoxContainer.new()
	capa_row.add_theme_constant_override("separation", 6)
	root.add_child(capa_row)
	capa_row.add_child(_label("Capa:"))
	_spin_capa = SpinBox.new()
	_spin_capa.min_value = 0
	_spin_capa.max_value = 4
	_spin_capa.value = 1
	_spin_capa.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	capa_row.add_child(_spin_capa)

	# Preview + summary
	root.add_child(_separator())

	_lbl_summary = Label.new()
	_lbl_summary.add_theme_font_size_override("font_size", 12)
	_lbl_summary.add_theme_color_override("font_color", Color(0.7, 0.7, 0.75))
	_lbl_summary.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(_lbl_summary)

	_lbl_warn = Label.new()
	_lbl_warn.add_theme_font_size_override("font_size", 11)
	_lbl_warn.add_theme_color_override("font_color", Color(0.9, 0.4, 0.3))
	_lbl_warn.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_lbl_warn.visible = false
	root.add_child(_lbl_warn)

	_preview = TextureRect.new()
	_preview.custom_minimum_size = Vector2(0, 180)
	_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root.add_child(_preview)

	# Split button
	_btn_split = Button.new()
	_btn_split.text = "Dividir en tiles"
	_btn_split.add_theme_font_size_override("font_size", 13)
	var split_bg := StyleBoxFlat.new()
	split_bg.bg_color = Color(0.25, 0.4, 0.6)
	split_bg.set_corner_radius_all(4)
	split_bg.set_content_margin_all(6)
	_btn_split.add_theme_stylebox_override("normal", split_bg)
	var split_hover := StyleBoxFlat.new()
	split_hover.bg_color = Color(0.3, 0.5, 0.7)
	split_hover.set_corner_radius_all(4)
	split_hover.set_content_margin_all(6)
	_btn_split.add_theme_stylebox_override("hover", split_hover)
	_btn_split.pressed.connect(_on_split)
	root.add_child(_btn_split)

	# Save button
	root.add_child(_separator())
	_btn_save = Button.new()
	_btn_save.text = "Aceptar"
	_btn_save.add_theme_font_size_override("font_size", 14)
	var save_bg := StyleBoxFlat.new()
	save_bg.bg_color = Color(0.2, 0.6, 0.3)
	save_bg.set_corner_radius_all(4)
	save_bg.set_content_margin_all(8)
	_btn_save.add_theme_stylebox_override("normal", save_bg)
	var save_hover := StyleBoxFlat.new()
	save_hover.bg_color = Color(0.25, 0.7, 0.35)
	save_hover.set_corner_radius_all(4)
	save_hover.set_content_margin_all(8)
	_btn_save.add_theme_stylebox_override("hover", save_hover)
	_btn_save.pressed.connect(_on_save)
	root.add_child(_btn_save)

	_lbl_save_hint = Label.new()
	_lbl_save_hint.text = "Escribe un nombre para continuar"
	_lbl_save_hint.add_theme_font_size_override("font_size", 11)
	_lbl_save_hint.add_theme_color_override("font_color", Color(0.6, 0.55, 0.4))
	_lbl_save_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(_lbl_save_hint)


func open_with_frame(frame: Dictionary, texture: ImageTexture, categories: PackedStringArray) -> void:
	_source_frame = frame.duplicate()
	_source_texture = texture

	var fw: int = frame.get("w", 0)
	var fh: int = frame.get("h", 0)

	# Pre-calculate tiles from frame size (default suggestion)
	var suggested_w := maxi(fw / 32, 1)
	var suggested_h := maxi(fh / 32, 1)

	# Set max based on frame pixel size / 32
	_spin_w.max_value = maxi(fw / 32, 1)
	_spin_h.max_value = maxi(fh / 32, 1)
	_spin_w.value = suggested_w
	_spin_h.value = suggested_h

	# Populate category dropdown
	_opt_type.clear()
	for cat in categories:
		_opt_type.add_item(cat)
	for i in range(_opt_type.item_count):
		if _opt_type.get_item_text(i) == "Terreno":
			_opt_type.select(i)
			break

	_input_name.text = ""
	_split_done = false
	var is_single := (suggested_w == 1 and suggested_h == 1)
	if is_single:
		_btn_split.visible = false
	else:
		_btn_split.visible = true
		_btn_split.disabled = false
		_btn_split.text = "Dividir en tiles"
	_on_tiles_changed()
	popup_centered()


func _on_tiles_changed() -> void:
	_tiles_w = int(_spin_w.value)
	_tiles_h = int(_spin_h.value)
	var is_single := (_tiles_w == 1 and _tiles_h == 1)
	# Changing tiles resets split state
	_split_done = false
	if is_single:
		_btn_split.visible = false
	else:
		_btn_split.visible = true
		_btn_split.disabled = false
		_btn_split.text = "Dividir en tiles"
	_validate_and_preview()
	_update_save_state()


func _validate_and_preview() -> void:
	if _source_texture == null or _source_frame.is_empty():
		return

	var fw: int = _source_frame.get("w", 0)
	var fh: int = _source_frame.get("h", 0)
	var is_single := (_tiles_w == 1 and _tiles_h == 1)
	var needed_w := _tiles_w * 32
	var needed_h := _tiles_h * 32

	# Validate: frame must be large enough for the tile grid
	var valid := true
	_lbl_warn.visible = false

	if not is_single:
		if needed_w > fw or needed_h > fh:
			_lbl_warn.text = "Frame %dx%d es menor que %dx%d px necesarios" % [fw, fh, needed_w, needed_h]
			_lbl_warn.visible = true
			valid = false
		elif fw % 32 != 0 or fh % 32 != 0:
			_lbl_warn.text = "Frame debe ser divisible por 32px (%dx%d)" % [fw, fh]
			_lbl_warn.visible = true
			valid = false

	if not valid:
		_btn_save.disabled = true
		_btn_split.disabled = true
	else:
		_btn_split.disabled = _split_done
	_update_preview()


func _update_preview() -> void:
	if _source_texture == null or _source_frame.is_empty():
		return

	var sx: int = _source_frame.get("sx", 0)
	var sy: int = _source_frame.get("sy", 0)
	var fw: int = _source_frame.get("w", 0)
	var fh: int = _source_frame.get("h", 0)
	var is_single := (_tiles_w == 1 and _tiles_h == 1)

	var img := _source_texture.get_image()
	if img == null:
		return

	if is_single:
		# 1x1: show the full frame as-is
		var crop_w := mini(fw, img.get_width() - sx)
		var crop_h := mini(fh, img.get_height() - sy)
		if crop_w > 0 and crop_h > 0:
			var region := img.get_region(Rect2i(sx, sy, crop_w, crop_h))
			_preview.texture = ImageTexture.create_from_image(region)
		_lbl_summary.text = "1×1 = 1 GRH (%dx%d px, sin dividir)" % [fw, fh]
	else:
		# NxM: show the tile grid area
		var pw := _tiles_w * 32
		var ph := _tiles_h * 32
		var crop_w := mini(pw, img.get_width() - sx)
		var crop_h := mini(ph, img.get_height() - sy)
		if crop_w > 0 and crop_h > 0:
			var region := img.get_region(Rect2i(sx, sy, crop_w, crop_h))
			_preview.texture = ImageTexture.create_from_image(region)
		var total := _tiles_w * _tiles_h
		_lbl_summary.text = "%d×%d = %d GRHs de 32×32 (%dx%d px)" % [_tiles_w, _tiles_h, total, pw, ph]


func _on_split() -> void:
	if _btn_split.disabled:
		return
	var is_single := (_tiles_w == 1 and _tiles_h == 1)
	if is_single:
		# 1x1: no real split needed, just mark as done
		_split_done = true
		_btn_split.disabled = true
		_btn_split.text = "Sin subdivisión (1×1)"
	else:
		split_requested.emit(_source_frame, _tiles_w, _tiles_h)
		_split_done = true
		_btn_split.disabled = true
		_btn_split.text = "Dividido ✓"
	_update_save_state()


func _update_save_state() -> void:
	var name_ok := not _input_name.text.strip_edges().is_empty()
	var is_single := (_tiles_w == 1 and _tiles_h == 1)
	# 1x1 doesn't need split; NxM requires split first
	var split_ok := is_single or _split_done
	_btn_save.disabled = not (name_ok and split_ok)
	_lbl_save_hint.visible = not name_ok


func _on_save() -> void:
	if _btn_save.disabled:
		return
	var name_text: String = _input_name.text.strip_edges()
	var category: String = _opt_type.get_item_text(_opt_type.selected) if _opt_type.selected >= 0 else "Otros"
	var capa: int = int(_spin_capa.value)

	if name_text.is_empty():
		name_text = "Textura %dx%d" % [_tiles_w, _tiles_h]

	confirmed.emit(name_text, category, capa)
	hide()


func get_tiles() -> Vector2i:
	return Vector2i(_tiles_w, _tiles_h)


func get_source_frame() -> Dictionary:
	return _source_frame


func _label(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 13)
	lbl.custom_minimum_size.x = 65
	return lbl


func _separator() -> HSeparator:
	var sep := HSeparator.new()
	sep.add_theme_constant_override("separation", 4)
	return sep
