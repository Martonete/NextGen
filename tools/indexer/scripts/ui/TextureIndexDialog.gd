## TextureIndexDialog.gd — Formulario para indexar una textura multi-tile
class_name TextureIndexDialog
extends Window

signal confirmed(name: String, category: String, capa: int)

var _input_name: LineEdit
var _opt_type: OptionButton
var _spin_capa: SpinBox
var _preview: TextureRect
var _lbl_summary: Label
var _btn_save: Button

# Source frame info (set before popup)
var _source_frame: Dictionary = {}
var _source_texture: ImageTexture = null
var _tiles_w: int = 0
var _tiles_h: int = 0


func _ready() -> void:
	title = "Indexar Textura"
	size = Vector2i(420, 480)
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
	name_row.add_child(_input_name)

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

	# Preview
	root.add_child(_separator())

	_lbl_summary = Label.new()
	_lbl_summary.add_theme_font_size_override("font_size", 12)
	_lbl_summary.add_theme_color_override("font_color", Color(0.7, 0.7, 0.75))
	_lbl_summary.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	root.add_child(_lbl_summary)

	_preview = TextureRect.new()
	_preview.custom_minimum_size = Vector2(0, 180)
	_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root.add_child(_preview)

	# Save button
	root.add_child(_separator())
	_btn_save = Button.new()
	_btn_save.text = "Guardar Textura"
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


func open_with_frame(frame: Dictionary, texture: ImageTexture, categories: PackedStringArray) -> void:
	_source_frame = frame.duplicate()
	_source_texture = texture

	var fw: int = frame.get("w", 0)
	var fh: int = frame.get("h", 0)
	_tiles_w = maxi(fw / 32, 1)
	_tiles_h = maxi(fh / 32, 1)

	# Validate: frame must be divisible by 32
	if fw % 32 != 0 or fh % 32 != 0 or fw < 32 or fh < 32:
		_lbl_summary.text = "El frame debe ser divisible por 32px (%dx%d)" % [fw, fh]
		_btn_save.disabled = true
	else:
		_btn_save.disabled = false

	# Populate category dropdown
	_opt_type.clear()
	for cat in categories:
		_opt_type.add_item(cat)
	for i in range(_opt_type.item_count):
		if _opt_type.get_item_text(i) == "Terreno":
			_opt_type.select(i)
			break

	_input_name.text = ""
	_update_preview()
	popup_centered()


func _update_preview() -> void:
	if _source_texture == null or _source_frame.is_empty():
		return

	var sx: int = _source_frame.get("sx", 0)
	var sy: int = _source_frame.get("sy", 0)
	var fw: int = _source_frame.get("w", 0)
	var fh: int = _source_frame.get("h", 0)
	var total := _tiles_w * _tiles_h

	var img := _source_texture.get_image()
	if img == null:
		return

	# Crop the region from the source image
	var crop_w := mini(fw, img.get_width() - sx)
	var crop_h := mini(fh, img.get_height() - sy)
	if crop_w > 0 and crop_h > 0:
		var region := img.get_region(Rect2i(sx, sy, crop_w, crop_h))
		_preview.texture = ImageTexture.create_from_image(region)

	_lbl_summary.text = "Textura %d×%d = %d GRHs de 32×32 (%dx%d px)" % [_tiles_w, _tiles_h, total, fw, fh]


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
