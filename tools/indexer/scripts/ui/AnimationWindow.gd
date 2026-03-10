## AnimationTab.gd — Tab de animacion + creacion de assets (sidebar)
class_name AnimationWindow
extends VBoxContainer

signal create_anim_pressed(indices: Array[int], speed: float)
signal save_body_pressed(walk_n: int, walk_e: int, walk_s: int, walk_w: int, off_x: int, off_y: int)
signal save_head_pressed(head_n: int, head_e: int, head_s: int, head_w: int)
signal save_helmet_pressed(head_n: int, head_e: int, head_s: int, head_w: int)
signal save_weapon_pressed(dir_n: int, dir_e: int, dir_s: int, dir_w: int)
signal save_shield_pressed(dir_n: int, dir_e: int, dir_s: int, dir_w: int)
signal save_fx_pressed(anim_grh: int, off_x: int, off_y: int)

var _frames: Array = []
var _sequence: Array[int] = []
var _texture: ImageTexture = null
var _textures: Dictionary = {}

# UI - animation creator
var _preview: FramePreviewPanel
var _seq_list: VBoxContainer
var _seq_scroll: ScrollContainer
var _speed_spin: SpinBox
var _btn_create: Button
var _lbl_info: Label
var _empty_label: Label
var _anim_section: VBoxContainer  # Wraps preview+list+speed+create

# UI - asset creator
var _asset_section: VBoxContainer
var _asset_forms: Dictionary = {}  # type_name -> VBoxContainer
var _active_form: String = ""

# Playback
var _playing: bool = true
var _play_time: float = 0.0
var _play_idx: int = 0


func _ready() -> void:
	name = "Animación"
	size_flags_vertical = Control.SIZE_EXPAND_FILL

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)

	var root := VBoxContainer.new()
	root.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root.add_theme_constant_override("separation", 6)
	scroll.add_child(root)

	# ── Empty state ──
	_empty_label = IndexerTheme.label(
		"Seleccioná 2+ frames con CTRL+click\npara crear una animación",
		IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM)
	_empty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_empty_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_empty_label.custom_minimum_size.y = 60
	root.add_child(_empty_label)

	# ── Animation creator section ──
	_anim_section = VBoxContainer.new()
	_anim_section.add_theme_constant_override("separation", 4)
	root.add_child(_anim_section)

	_preview = FramePreviewPanel.new()
	_preview.custom_minimum_size = Vector2(0, 160)
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_anim_section.add_child(_preview)

	_lbl_info = IndexerTheme.label("0 frames", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM)
	_anim_section.add_child(_lbl_info)

	_seq_scroll = ScrollContainer.new()
	_seq_scroll.custom_minimum_size.y = 80
	_seq_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_seq_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_anim_section.add_child(_seq_scroll)

	_seq_list = VBoxContainer.new()
	_seq_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_seq_list.add_theme_constant_override("separation", 2)
	_seq_scroll.add_child(_seq_list)

	var bottom := HBoxContainer.new()
	bottom.add_theme_constant_override("separation", 4)
	_anim_section.add_child(bottom)

	bottom.add_child(IndexerTheme.label("Vel.", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))

	_speed_spin = IndexerTheme.spinbox(10, 99999, 500)
	_speed_spin.step = 10
	_speed_spin.suffix = " ms"
	_speed_spin.custom_minimum_size.x = 90
	bottom.add_child(_speed_spin)

	bottom.add_child(IndexerTheme.spacer())

	_btn_create = IndexerTheme.success_button("Crear GRH Anim", _on_create)
	bottom.add_child(_btn_create)

	_show_empty(true)

	# ── Separator ──
	root.add_child(IndexerTheme.separator_h())

	# ── Asset creation section ──
	root.add_child(IndexerTheme.section_label("Indexar Asset"))

	var btn_row1 := HBoxContainer.new()
	btn_row1.add_theme_constant_override("separation", 4)
	root.add_child(btn_row1)

	var btn_row2 := HBoxContainer.new()
	btn_row2.add_theme_constant_override("separation", 4)
	root.add_child(btn_row2)

	_add_asset_btn(btn_row1, "Body", "body")
	_add_asset_btn(btn_row1, "Cabeza", "head")
	_add_asset_btn(btn_row1, "Casco", "helmet")
	_add_asset_btn(btn_row2, "Arma", "weapon")
	_add_asset_btn(btn_row2, "Escudo", "shield")
	_add_asset_btn(btn_row2, "FX", "fx")

	_asset_section = VBoxContainer.new()
	_asset_section.add_theme_constant_override("separation", 4)
	root.add_child(_asset_section)

	_build_directional_form("body", "personajes.ind", true)
	_build_directional_form("head", "cabezas.ind", false)
	_build_directional_form("helmet", "cascos.ind", false)
	_build_directional_form("weapon", "armas.dat", false)
	_build_directional_form("shield", "escudos.dat", false)
	_build_fx_form()


func _add_asset_btn(parent: HBoxContainer, label: String, type: String) -> void:
	var btn := Button.new()
	btn.text = label
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.custom_minimum_size.y = 28
	btn.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	btn.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 4, 2))
	btn.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 4, 2))
	btn.add_theme_stylebox_override("pressed", IndexerTheme._flat_box(IndexerTheme.ACCENT, 3, 4, 2))
	btn.pressed.connect(func(): _toggle_form(type))
	parent.add_child(btn)


func _build_directional_form(type: String, file_label: String, has_offset: bool) -> void:
	var form := VBoxContainer.new()
	form.add_theme_constant_override("separation", 4)
	form.visible = false
	_asset_section.add_child(form)
	_asset_forms[type] = form

	form.add_child(IndexerTheme.label(file_label, IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))

	var dirs := ["Norte", "Este", "Sur", "Oeste"]
	var spins: Array[SpinBox] = []
	for d in dirs:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		form.add_child(row)
		var lbl := IndexerTheme.label(d + ":", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
		lbl.custom_minimum_size.x = 50
		row.add_child(lbl)
		var spin := IndexerTheme.spinbox(0, 999999, 0)
		spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		spin.prefix = "GRH "
		row.add_child(spin)
		spins.append(spin)

	if has_offset:
		var off_row := HBoxContainer.new()
		off_row.add_theme_constant_override("separation", 4)
		form.add_child(off_row)
		var lbl_x := IndexerTheme.label("Head X:", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
		lbl_x.custom_minimum_size.x = 50
		off_row.add_child(lbl_x)
		var spin_x := IndexerTheme.spinbox(-200, 200, 0)
		spin_x.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		off_row.add_child(spin_x)
		var lbl_y := IndexerTheme.label("Y:", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
		off_row.add_child(lbl_y)
		var spin_y := IndexerTheme.spinbox(-200, 200, 0)
		spin_y.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		off_row.add_child(spin_y)
		spins.append(spin_x)
		spins.append(spin_y)

	var save_btn := IndexerTheme.success_button("Guardar", func(): _on_save_asset(type, spins))
	form.add_child(save_btn)

	# Store spins ref
	form.set_meta("spins", spins)


func _build_fx_form() -> void:
	var form := VBoxContainer.new()
	form.add_theme_constant_override("separation", 4)
	form.visible = false
	_asset_section.add_child(form)
	_asset_forms["fx"] = form

	form.add_child(IndexerTheme.label("FXs.ind", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))

	var spins: Array[SpinBox] = []

	var row_anim := HBoxContainer.new()
	row_anim.add_theme_constant_override("separation", 4)
	form.add_child(row_anim)
	var lbl_a := IndexerTheme.label("Anim:", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
	lbl_a.custom_minimum_size.x = 50
	row_anim.add_child(lbl_a)
	var spin_anim := IndexerTheme.spinbox(0, 999999, 0)
	spin_anim.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	spin_anim.prefix = "GRH "
	row_anim.add_child(spin_anim)
	spins.append(spin_anim)

	var row_off := HBoxContainer.new()
	row_off.add_theme_constant_override("separation", 4)
	form.add_child(row_off)
	var lbl_x := IndexerTheme.label("Off X:", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
	lbl_x.custom_minimum_size.x = 50
	row_off.add_child(lbl_x)
	var spin_x := IndexerTheme.spinbox(-200, 200, 0)
	spin_x.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row_off.add_child(spin_x)
	var lbl_y := IndexerTheme.label("Y:", IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
	row_off.add_child(lbl_y)
	var spin_y := IndexerTheme.spinbox(-200, 200, 0)
	spin_y.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row_off.add_child(spin_y)
	spins.append(spin_x)
	spins.append(spin_y)

	var save_btn := IndexerTheme.success_button("Guardar", func(): _on_save_asset("fx", spins))
	form.add_child(save_btn)

	form.set_meta("spins", spins)


func _toggle_form(type: String) -> void:
	if _active_form == type:
		_asset_forms[type].visible = false
		_active_form = ""
		return
	for key in _asset_forms:
		_asset_forms[key].visible = (key == type)
	_active_form = type


func _on_save_asset(type: String, spins: Array[SpinBox]) -> void:
	match type:
		"body":
			save_body_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value), int(spins[3].value),
				int(spins[4].value), int(spins[5].value))
		"head":
			save_head_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value), int(spins[3].value))
		"helmet":
			save_helmet_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value), int(spins[3].value))
		"weapon":
			save_weapon_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value), int(spins[3].value))
		"shield":
			save_shield_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value), int(spins[3].value))
		"fx":
			save_fx_pressed.emit(
				int(spins[0].value), int(spins[1].value),
				int(spins[2].value))


# ── Animation creator methods ──

func _show_empty(empty: bool) -> void:
	_empty_label.visible = empty
	_anim_section.visible = not empty


func set_frames(frames: Array, texture: ImageTexture, textures: Dictionary) -> void:
	_frames = frames.duplicate()
	_texture = texture
	_textures = textures.duplicate()

	_sequence.clear()
	for f in _frames:
		var grh: int = f.get("grh_index", 0)
		if grh > 0:
			_sequence.append(grh)

	_play_idx = 0
	_play_time = 0.0
	_playing = true

	_preview.set_image(_texture.get_image() if _texture != null else null)
	if not _textures.is_empty():
		_preview.set_textures(_textures)

	_show_empty(_sequence.size() < 2)
	_rebuild_list()
	_update_preview()


func clear() -> void:
	_frames.clear()
	_sequence.clear()
	_show_empty(true)
	for c in _seq_list.get_children():
		c.queue_free()


func _rebuild_list() -> void:
	for c in _seq_list.get_children():
		c.queue_free()

	_lbl_info.text = "%d frames" % _sequence.size()
	_btn_create.disabled = _sequence.size() < 2

	for i in range(_sequence.size()):
		_build_row(i)


func _build_row(idx: int) -> void:
	var grh_idx: int = _sequence[idx]

	var frame_dict: Dictionary = {}
	for f in _frames:
		if f.get("grh_index", 0) == grh_idx:
			frame_dict = f
			break

	var row_bg := StyleBoxFlat.new()
	row_bg.bg_color = IndexerTheme.BG_SECTION
	row_bg.set_corner_radius_all(3)
	row_bg.set_content_margin_all(2)

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", row_bg)
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 3)
	panel.add_child(hbox)

	hbox.add_child(_make_thumb(frame_dict))

	var w: int = frame_dict.get("w", 0)
	var h: int = frame_dict.get("h", 0)
	var lbl := IndexerTheme.label("G%d %dx%d" % [grh_idx, w, h], IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(lbl)

	var btn_up := _mini_btn("▲", idx == 0)
	var up_idx := idx
	btn_up.pressed.connect(func(): _on_move(up_idx, -1))
	hbox.add_child(btn_up)

	var btn_down := _mini_btn("▼", idx == _sequence.size() - 1)
	var down_idx := idx
	btn_down.pressed.connect(func(): _on_move(down_idx, 1))
	hbox.add_child(btn_down)

	var btn_remove := _mini_btn("✕", false)
	btn_remove.add_theme_color_override("font_color", IndexerTheme.TEXT_DANGER)
	var rem_idx := idx
	btn_remove.pressed.connect(func(): _on_remove(rem_idx))
	hbox.add_child(btn_remove)

	_seq_list.add_child(panel)


func _mini_btn(text: String, disabled: bool) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(22, 22)
	btn.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	btn.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 3, 1))
	btn.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 3, 1))
	btn.disabled = disabled
	return btn


func _update_preview() -> void:
	var preview_frames: Array = []
	for grh_idx in _sequence:
		for f in _frames:
			if f.get("grh_index", 0) == grh_idx:
				preview_frames.append(f)
				break
	_preview.set_frames(preview_frames)
	if not preview_frames.is_empty():
		_play_idx = clampi(_play_idx, 0, preview_frames.size() - 1)
		_preview.show_frame(_play_idx)


func _on_move(idx: int, dir: int) -> void:
	var target := idx + dir
	if target < 0 or target >= _sequence.size():
		return
	var tmp: int = _sequence[idx]
	_sequence[idx] = _sequence[target]
	_sequence[target] = tmp
	_rebuild_list()
	_update_preview()


func _on_remove(idx: int) -> void:
	if idx < 0 or idx >= _sequence.size():
		return
	_sequence.remove_at(idx)
	_rebuild_list()
	_update_preview()
	if _sequence.size() < 2:
		_show_empty(true)


func _on_create() -> void:
	if _sequence.size() < 2:
		return
	var indices: Array[int] = []
	for grh in _sequence:
		indices.append(grh)
	create_anim_pressed.emit(indices, _speed_spin.value)


func _make_thumb(f: Dictionary) -> TextureRect:
	var tr := TextureRect.new()
	tr.custom_minimum_size = Vector2(28, 28)
	tr.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	tr.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	if _texture != null and f.get("w", 0) > 0 and f.get("h", 0) > 0:
		var tex: ImageTexture = _texture
		var fnum: int = f.get("file_num", 0)
		if fnum > 0 and _textures.has(fnum):
			tex = _textures[fnum]
		if tex != null:
			var img := tex.get_image()
			if img != null:
				var sx2 := mini(f.get("sx", 0) + f.get("w", 0), img.get_width())
				var sy2 := mini(f.get("sy", 0) + f.get("h", 0), img.get_height())
				if sx2 > f.get("sx", 0) and sy2 > f.get("sy", 0):
					var crop := img.get_region(Rect2i(f.get("sx", 0), f.get("sy", 0), sx2 - f.get("sx", 0), sy2 - f.get("sy", 0)))
					tr.texture = ImageTexture.create_from_image(crop)
	return tr


func _process(delta: float) -> void:
	if not visible or not _anim_section.visible:
		return
	if not _playing or _sequence.size() <= 1:
		return

	var speed := maxf(_speed_spin.value, 10.0)
	var fps := maxf(_sequence.size() * 1000.0 / speed, 1.0)
	var dur := 1.0 / fps

	_play_time += delta
	if _play_time >= dur:
		_play_time = fmod(_play_time, dur)
		_play_idx = (_play_idx + 1) % _sequence.size()
		_preview.show_frame(_play_idx)
