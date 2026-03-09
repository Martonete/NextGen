## InspectorPanel.gd — Right-side docked panel with tabs
class_name InspectorPanel
extends VBoxContainer

# ── Signals (forwarded from sub-tabs) ─────────────────────────────
signal frame_selected(idx: int)
signal frame_deleted(idx: int)
signal frame_props_changed(idx: int, sx: int, sy: int, w: int, h: int, grh: int)
signal clear_frames_pressed
signal detect_grid_pressed(cell_w: int, cell_h: int, off_x: int, off_y: int, mrg_x: int, mrg_y: int, skip_empty: bool)
signal detect_blobs_pressed(alpha: float, min_size: int, padding: int)
signal create_anim_pressed(indices: Array[int], speed: float)
signal split_frame_pressed(cell_w: int, cell_h: int)
signal save_init_pressed(path: String, content: String)
signal add_manual_frame_pressed
signal next_grh_changed(val: int)

var _tabs: TabContainer

# ── Frames tab ──
var _frame_list_vbox: VBoxContainer
var _lbl_frame_count: Label

# ── Props tab ──
var _spin_sx: SpinBox
var _spin_sy: SpinBox
var _spin_fw: SpinBox
var _spin_fh: SpinBox
var _spin_grh: SpinBox
var _lbl_prop_info: Label
var _props_updating: bool = false

# ── Preview tab ──
var _preview: FramePreviewPanel
var _btn_play: Button
var _lbl_anim_info: Label
var _spin_anim_fps: SpinBox
var _anim_playing: bool = false
var _anim_time: float = 0.0
var _anim_fps: float = 8.0
var _anim_frame_idx: int = 0
var _frames_for_anim: Array = []

# ── Detection tab ──
var _spin_cell_w: SpinBox
var _spin_cell_h: SpinBox
var _spin_off_x: SpinBox
var _spin_off_y: SpinBox
var _spin_mrg_x: SpinBox
var _spin_mrg_y: SpinBox
var _chk_skip_empty: CheckButton
var _spin_alpha: SpinBox
var _spin_min_size: SpinBox
var _spin_padding: SpinBox

# ── GRH Viewer tab ──
var _grh_viewer_vbox: VBoxContainer
var _lbl_grh_info: Label

# ── INIT tab ──
var _init_file_list: ItemList
var _init_text_edit: TextEdit
var _init_files: Array[String] = []
var _init_current_path: String = ""

# ── Anim Creator tab ──
var _edit_anim_indices: LineEdit
var _spin_anim_speed: SpinBox

# ── Split tab ──
var _spin_split_w: SpinBox
var _spin_split_h: SpinBox

# ── Config ──
var _spin_next_grh: SpinBox
var _spin_filenum: SpinBox
var _lbl_max_grh: Label


func _ready() -> void:
	custom_minimum_size.x = 340
	add_theme_constant_override("separation", 0)

	_tabs = TabContainer.new()
	_tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_tabs.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	add_child(_tabs)

	_tabs.add_child(_build_frames_tab())
	_tabs.add_child(_build_props_tab())
	_tabs.add_child(_build_preview_tab())
	_tabs.add_child(_build_detect_tab())
	_tabs.add_child(_build_grh_viewer_tab())
	_tabs.add_child(_build_init_tab())
	_tabs.add_child(_build_anim_tab())
	_tabs.add_child(_build_config_tab())


# ── Public API ────────────────────────────────────────────────────

func update_frames(frames: Array, selected: int) -> void:
	_rebuild_frame_list(frames, selected)
	_frames_for_anim = frames
	_preview.set_frames(frames)
	if selected >= 0 and selected < frames.size():
		_preview.show_frame(selected)

func update_selected_props(frame: Dictionary) -> void:
	_props_updating = true
	_spin_sx.value = frame.get("sx", 0)
	_spin_sy.value = frame.get("sy", 0)
	_spin_fw.value = frame.get("w", 32)
	_spin_fh.value = frame.get("h", 32)
	_spin_grh.value = frame.get("grh_index", 1)
	_lbl_prop_info.text = "GRH %d — %dx%d" % [frame.get("grh_index", 0), frame.get("w", 0), frame.get("h", 0)]
	_props_updating = false

func clear_props() -> void:
	_lbl_prop_info.text = "(ninguno seleccionado)"

func set_image(img: Image) -> void:
	_preview.set_image(img)

func set_grh_data(max_idx: int, count: int) -> void:
	_lbl_max_grh.text = "Max GRH: %d (%d entradas)" % [max_idx, count]
	_spin_next_grh.value = max_idx + 1

func get_next_grh() -> int:
	return int(_spin_next_grh.value)

func set_next_grh(val: int) -> void:
	_spin_next_grh.value = val

func get_filenum_base() -> int:
	return int(_spin_filenum.value)

func show_frame_preview(idx: int) -> void:
	_preview.show_frame(idx)
	_anim_frame_idx = idx

func process_animation(delta: float) -> void:
	if not _anim_playing or _frames_for_anim.size() <= 1:
		return
	_anim_time += delta
	var dur := 1.0 / maxf(_anim_fps, 1.0)
	if _anim_time >= dur:
		_anim_time = fmod(_anim_time, dur)
		_anim_frame_idx = (_anim_frame_idx + 1) % _frames_for_anim.size()
		_preview.show_frame(_anim_frame_idx)
		_lbl_anim_info.text = "Frame %d / %d" % [_anim_frame_idx + 1, _frames_for_anim.size()]

func update_grh_viewer(entries: Array, texture: ImageTexture) -> void:
	for c in _grh_viewer_vbox.get_children():
		c.queue_free()
	_lbl_grh_info.text = "%d GRHs indexados" % entries.size()
	for e in entries:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		# Mini preview
		if texture != null and e.get("num_frames", 1) == 1:
			var preview_rect := TextureRect.new()
			var atlas := AtlasTexture.new()
			atlas.atlas = texture
			atlas.region = Rect2(e.get("sx", 0), e.get("sy", 0), e.get("width", 32), e.get("height", 32))
			preview_rect.texture = atlas
			preview_rect.expand_mode = TextureRect.EXPAND_FIT_WIDTH_PROPORTIONAL
			preview_rect.custom_minimum_size = Vector2(40, 40)
			row.add_child(preview_rect)
		var lbl := IndexerTheme.label(
			"G%d  %dx%d  (%d,%d)" % [e.get("grh_index", 0), e.get("width", 0), e.get("height", 0), e.get("sx", 0), e.get("sy", 0)],
			IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(lbl)
		_grh_viewer_vbox.add_child(row)

func load_init_files(folder: String) -> void:
	_init_files.clear()
	_init_file_list.clear()
	var dir := DirAccess.open(folder)
	if dir == null:
		return
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		var ext := f.get_extension().to_lower()
		if ext in ["ind", "ini", "dat"]:
			_init_files.append(folder.path_join(f))
			_init_file_list.add_item(f)
		f = dir.get_next()
	dir.list_dir_end()
	_init_files.sort()

func use_current_frames_for_anim(frames: Array) -> void:
	var indices: PackedStringArray = PackedStringArray()
	for f in frames:
		indices.append(str(f.get("grh_index", 0)))
	_edit_anim_indices.text = ", ".join(indices)


# ── Tab builders ──────────────────────────────────────────────────

func _build_frames_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Frames"
	vbox.add_theme_constant_override("separation", 4)

	_lbl_frame_count = IndexerTheme.label("0 frames", IndexerTheme.TEXT_SECONDARY)
	vbox.add_child(_lbl_frame_count)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(scroll)

	_frame_list_vbox = VBoxContainer.new()
	_frame_list_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_frame_list_vbox.add_theme_constant_override("separation", 1)
	scroll.add_child(_frame_list_vbox)

	var btn_row := HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 4)
	vbox.add_child(btn_row)
	btn_row.add_child(IndexerTheme.button("+ Manual", func(): add_manual_frame_pressed.emit(), IndexerTheme.TEXT_ACCENT))
	btn_row.add_child(IndexerTheme.spacer())
	btn_row.add_child(IndexerTheme.button("Limpiar", func(): clear_frames_pressed.emit(), IndexerTheme.TEXT_DANGER))

	return vbox


func _build_props_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Propiedades"
	vbox.add_theme_constant_override("separation", 6)

	_lbl_prop_info = IndexerTheme.label("(ninguno seleccionado)", IndexerTheme.TEXT_SECONDARY)
	vbox.add_child(_lbl_prop_info)

	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid)

	grid.add_child(IndexerTheme.label("X:", IndexerTheme.TEXT_SECONDARY))
	_spin_sx = IndexerTheme.spinbox(0, 16383, 0, func(_v): _on_props_changed())
	grid.add_child(_spin_sx)
	grid.add_child(IndexerTheme.label("Y:", IndexerTheme.TEXT_SECONDARY))
	_spin_sy = IndexerTheme.spinbox(0, 16383, 0, func(_v): _on_props_changed())
	grid.add_child(_spin_sy)

	grid.add_child(IndexerTheme.label("W:", IndexerTheme.TEXT_SECONDARY))
	_spin_fw = IndexerTheme.spinbox(1, 16383, 32, func(_v): _on_props_changed())
	grid.add_child(_spin_fw)
	grid.add_child(IndexerTheme.label("H:", IndexerTheme.TEXT_SECONDARY))
	_spin_fh = IndexerTheme.spinbox(1, 16383, 32, func(_v): _on_props_changed())
	grid.add_child(_spin_fh)

	grid.add_child(IndexerTheme.label("GRH:", IndexerTheme.TEXT_ACCENT))
	_spin_grh = IndexerTheme.spinbox(1, 999999, 1, func(_v): _on_props_changed())
	grid.add_child(_spin_grh)
	grid.add_child(Label.new())
	grid.add_child(Label.new())

	var btn_row := HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 4)
	vbox.add_child(btn_row)
	var btn_del := IndexerTheme.button("Eliminar frame", func(): frame_deleted.emit(-1), IndexerTheme.TEXT_DANGER)
	btn_row.add_child(btn_del)

	vbox.add_child(IndexerTheme.separator_h())
	vbox.add_child(IndexerTheme.heading("Dividir frame"))

	var split_row := HBoxContainer.new()
	split_row.add_theme_constant_override("separation", 4)
	vbox.add_child(split_row)
	split_row.add_child(IndexerTheme.label("W:", IndexerTheme.TEXT_SECONDARY))
	_spin_split_w = IndexerTheme.spinbox(1, 2048, 32)
	split_row.add_child(_spin_split_w)
	split_row.add_child(IndexerTheme.label("H:", IndexerTheme.TEXT_SECONDARY))
	_spin_split_h = IndexerTheme.spinbox(1, 2048, 32)
	split_row.add_child(_spin_split_h)

	var btn_split := IndexerTheme.button("Dividir en celdas", _on_split_btn, IndexerTheme.TEXT_ACCENT)
	vbox.add_child(btn_split)

	return vbox


func _build_preview_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Preview"
	vbox.add_theme_constant_override("separation", 4)

	_preview = FramePreviewPanel.new()
	_preview.custom_minimum_size = Vector2(0, 180)
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_preview.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(_preview)

	var bar := HBoxContainer.new()
	bar.add_theme_constant_override("separation", 3)
	vbox.add_child(bar)

	bar.add_child(IndexerTheme.icon_button("<", func(): _anim_step(-1), "Frame anterior", 26))

	_btn_play = Button.new()
	_btn_play.text = "Play"
	_btn_play.toggle_mode = true
	_btn_play.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	_btn_play.toggled.connect(func(on: bool):
		_anim_playing = on
		_anim_time = 0.0
		_btn_play.text = "Pausa" if on else "Play")
	bar.add_child(_btn_play)

	bar.add_child(IndexerTheme.icon_button(">", func(): _anim_step(1), "Frame siguiente", 26))

	bar.add_child(IndexerTheme.label("FPS:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_anim_fps = IndexerTheme.spinbox(1, 60, 8, func(v): _anim_fps = v)
	_spin_anim_fps.custom_minimum_size.x = 52
	bar.add_child(_spin_anim_fps)

	_lbl_anim_info = IndexerTheme.label("--", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	_lbl_anim_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.add_child(_lbl_anim_info)

	return vbox


func _build_detect_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Deteccion"
	vbox.add_theme_constant_override("separation", 6)

	# Grid section
	vbox.add_child(IndexerTheme.heading("Cuadricula"))
	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid)

	grid.add_child(IndexerTheme.label("W:", IndexerTheme.TEXT_SECONDARY))
	_spin_cell_w = IndexerTheme.spinbox(1, 2048, 32)
	grid.add_child(_spin_cell_w)
	grid.add_child(IndexerTheme.label("H:", IndexerTheme.TEXT_SECONDARY))
	_spin_cell_h = IndexerTheme.spinbox(1, 2048, 32)
	grid.add_child(_spin_cell_h)

	grid.add_child(IndexerTheme.label("OffX:", IndexerTheme.TEXT_MUTED))
	_spin_off_x = IndexerTheme.spinbox(0, 2048, 0)
	grid.add_child(_spin_off_x)
	grid.add_child(IndexerTheme.label("OffY:", IndexerTheme.TEXT_MUTED))
	_spin_off_y = IndexerTheme.spinbox(0, 2048, 0)
	grid.add_child(_spin_off_y)

	grid.add_child(IndexerTheme.label("MrgX:", IndexerTheme.TEXT_MUTED))
	_spin_mrg_x = IndexerTheme.spinbox(0, 512, 0)
	grid.add_child(_spin_mrg_x)
	grid.add_child(IndexerTheme.label("MrgY:", IndexerTheme.TEXT_MUTED))
	_spin_mrg_y = IndexerTheme.spinbox(0, 512, 0)
	grid.add_child(_spin_mrg_y)

	_chk_skip_empty = CheckButton.new()
	_chk_skip_empty.text = "Saltar celdas vacias"
	_chk_skip_empty.button_pressed = true
	_chk_skip_empty.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	vbox.add_child(_chk_skip_empty)

	# Presets — common AO sprite types
	vbox.add_child(IndexerTheme.label("Presets:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	var presets_row1 := HBoxContainer.new()
	presets_row1.add_theme_constant_override("separation", 3)
	vbox.add_child(presets_row1)
	# Body/weapon/movement sprites
	var p_body := IndexerTheme.icon_button("Cuerpo 25x45", _make_preset_cb(25, 45), "Cuerpos, armas, movimientos", 90)
	presets_row1.add_child(p_body)
	var p_head := IndexerTheme.icon_button("Cabeza 16x16", _make_preset_cb(16, 16), "Cabezas", 90)
	presets_row1.add_child(p_head)
	var p_shield := IndexerTheme.icon_button("Escudo 25x25", _make_preset_cb(25, 25), "Escudos", 90)
	presets_row1.add_child(p_shield)

	var presets_row2 := HBoxContainer.new()
	presets_row2.add_theme_constant_override("separation", 3)
	vbox.add_child(presets_row2)
	# Tile textures (pow2)
	for p in [[32,32],[64,64],[128,128],[256,256]]:
		var pw: int = p[0]; var ph: int = p[1]
		var pbtn := IndexerTheme.icon_button("%dx%d" % [pw, ph], _make_preset_cb(pw, ph), "Tiles %dx%d" % [pw, ph], 56)
		presets_row2.add_child(pbtn)

	var btn_grid := IndexerTheme.button("Detectar grid", _on_detect_grid_btn, IndexerTheme.TEXT_ACCENT)
	vbox.add_child(btn_grid)

	vbox.add_child(IndexerTheme.separator_h())

	# Blob section
	vbox.add_child(IndexerTheme.heading("Blobs (alfa)"))
	var bgrid := GridContainer.new()
	bgrid.columns = 4
	bgrid.add_theme_constant_override("h_separation", 4)
	bgrid.add_theme_constant_override("v_separation", 4)
	vbox.add_child(bgrid)

	bgrid.add_child(IndexerTheme.label("Alpha%:", IndexerTheme.TEXT_SECONDARY))
	_spin_alpha = IndexerTheme.spinbox(0, 100, 3)
	bgrid.add_child(_spin_alpha)
	bgrid.add_child(IndexerTheme.label("MinPx:", IndexerTheme.TEXT_SECONDARY))
	_spin_min_size = IndexerTheme.spinbox(1, 512, 4)
	bgrid.add_child(_spin_min_size)
	bgrid.add_child(IndexerTheme.label("Pad:", IndexerTheme.TEXT_SECONDARY))
	_spin_padding = IndexerTheme.spinbox(0, 64, 1)
	bgrid.add_child(_spin_padding)
	bgrid.add_child(Label.new())
	bgrid.add_child(Label.new())

	var btn_blobs := IndexerTheme.button("Detectar blobs", _on_detect_blobs_btn, IndexerTheme.TEXT_WARNING)
	vbox.add_child(btn_blobs)

	return vbox


func _build_grh_viewer_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "GRH Index"
	vbox.add_theme_constant_override("separation", 4)

	_lbl_grh_info = IndexerTheme.label("Selecciona una imagen", IndexerTheme.TEXT_SECONDARY)
	vbox.add_child(_lbl_grh_info)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(scroll)

	_grh_viewer_vbox = VBoxContainer.new()
	_grh_viewer_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_grh_viewer_vbox.add_theme_constant_override("separation", 2)
	scroll.add_child(_grh_viewer_vbox)

	return vbox


func _build_init_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "INIT"

	var vsplit := VSplitContainer.new()
	vsplit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vsplit.split_offset = 140
	vbox.add_child(vsplit)

	var top := VBoxContainer.new()
	top.add_theme_constant_override("separation", 4)
	vsplit.add_child(top)

	_init_file_list = ItemList.new()
	_init_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_file_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_init_file_list.item_selected.connect(_on_init_file_selected)
	top.add_child(_init_file_list)

	var btn_row := HBoxContainer.new()
	btn_row.add_theme_constant_override("separation", 4)
	top.add_child(btn_row)
	var btn_save := IndexerTheme.button("Guardar", _on_save_init_btn, IndexerTheme.TEXT_SUCCESS)
	btn_row.add_child(btn_save)
	btn_row.add_child(IndexerTheme.button("Recargar", func():
		if not _init_current_path.is_empty():
			_load_init_file(_init_current_path)))

	_init_text_edit = TextEdit.new()
	_init_text_edit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_text_edit.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_init_text_edit.placeholder_text = "Selecciona un archivo INIT"
	vsplit.add_child(_init_text_edit)

	return vbox


func _build_anim_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Animacion"
	vbox.add_theme_constant_override("separation", 6)

	vbox.add_child(IndexerTheme.label("Indices GRH separados por coma:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))

	_edit_anim_indices = LineEdit.new()
	_edit_anim_indices.placeholder_text = "Ej: 1001, 1002, 1003"
	_edit_anim_indices.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	vbox.add_child(_edit_anim_indices)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	vbox.add_child(row)
	row.add_child(IndexerTheme.label("Speed:", IndexerTheme.TEXT_SECONDARY))
	_spin_anim_speed = IndexerTheme.spinbox(0.001, 999.0, 8.0)
	_spin_anim_speed.step = 0.1
	row.add_child(_spin_anim_speed)
	row.add_child(IndexerTheme.button("Usar frames actuales", func():
		# Signal to Main to fill indices
		pass))

	var btn_create := IndexerTheme.button("Crear GRH Animado", _on_create_anim_btn, IndexerTheme.TEXT_SUCCESS)
	vbox.add_child(btn_create)

	return vbox


func _build_config_tab() -> Control:
	var vbox := VBoxContainer.new()
	vbox.name = "Config"
	vbox.add_theme_constant_override("separation", 6)

	_lbl_max_grh = IndexerTheme.label("Max GRH: --", IndexerTheme.TEXT_SUCCESS)
	vbox.add_child(_lbl_max_grh)

	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 8)
	grid.add_theme_constant_override("v_separation", 6)
	vbox.add_child(grid)

	grid.add_child(IndexerTheme.label("Proximo GRH:", IndexerTheme.TEXT_SECONDARY))
	_spin_next_grh = IndexerTheme.spinbox(1, 999999, 1, func(v): next_grh_changed.emit(int(v)))
	grid.add_child(_spin_next_grh)

	grid.add_child(IndexerTheme.label("FileNum base:", IndexerTheme.TEXT_SECONDARY))
	_spin_filenum = IndexerTheme.spinbox(1, 99999, 1)
	grid.add_child(_spin_filenum)

	return vbox


# ── Internal helpers ──────────────────────────────────────────────

func _rebuild_frame_list(frames: Array, selected: int) -> void:
	for c in _frame_list_vbox.get_children():
		c.free()
	_lbl_frame_count.text = "%d frames" % frames.size()

	for i in range(frames.size()):
		var f: Dictionary = frames[i]
		var is_sel := (i == selected)
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 2)

		var col := IndexerTheme.TEXT_ACCENT if is_sel else IndexerTheme.TEXT_PRIMARY
		var lbl := IndexerTheme.label(
			"G%d  (%d,%d)  %dx%d" % [f.get("grh_index", 0), f.get("sx", 0), f.get("sy", 0), f.get("w", 0), f.get("h", 0)],
			col, IndexerTheme.FONT_SIZE_SM)
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(lbl)

		var idx := i
		var btn_sel := IndexerTheme.icon_button("Sel", func(): frame_selected.emit(idx), "", 28)
		btn_sel.add_theme_font_size_override("font_size", 9)
		row.add_child(btn_sel)

		var btn_del := IndexerTheme.icon_button("X", func(): frame_deleted.emit(idx), "", 24)
		btn_del.add_theme_font_size_override("font_size", 9)
		btn_del.add_theme_color_override("font_color", IndexerTheme.TEXT_DANGER)
		row.add_child(btn_del)

		_frame_list_vbox.add_child(row)


func _make_preset_cb(pw: int, ph: int) -> Callable:
	return func():
		_spin_cell_w.value = pw
		_spin_cell_h.value = ph

func _on_detect_grid_btn() -> void:
	detect_grid_pressed.emit(
		int(_spin_cell_w.value), int(_spin_cell_h.value),
		int(_spin_off_x.value), int(_spin_off_y.value),
		int(_spin_mrg_x.value), int(_spin_mrg_y.value),
		_chk_skip_empty.button_pressed)

func _on_detect_blobs_btn() -> void:
	detect_blobs_pressed.emit(
		_spin_alpha.value / 100.0,
		int(_spin_min_size.value),
		int(_spin_padding.value))

func _on_save_init_btn() -> void:
	if not _init_current_path.is_empty():
		save_init_pressed.emit(_init_current_path, _init_text_edit.text)

func _on_split_btn() -> void:
	split_frame_pressed.emit(int(_spin_split_w.value), int(_spin_split_h.value))

func _on_create_anim_btn() -> void:
	var parts := _edit_anim_indices.text.split(",")
	var indices: Array[int] = []
	for p in parts:
		var v := p.strip_edges().to_int()
		if v > 0:
			indices.append(v)
	if indices.size() >= 2:
		create_anim_pressed.emit(indices, _spin_anim_speed.value)

func _on_props_changed() -> void:
	if _props_updating:
		return
	frame_props_changed.emit(-1, int(_spin_sx.value), int(_spin_sy.value), int(_spin_fw.value), int(_spin_fh.value), int(_spin_grh.value))


func _anim_step(dir: int) -> void:
	if _frames_for_anim.is_empty():
		return
	_anim_frame_idx = clampi(_anim_frame_idx + dir, 0, _frames_for_anim.size() - 1)
	_preview.show_frame(_anim_frame_idx)
	_lbl_anim_info.text = "Frame %d / %d" % [_anim_frame_idx + 1, _frames_for_anim.size()]


func _on_init_file_selected(idx: int) -> void:
	if idx < 0 or idx >= _init_files.size():
		return
	_load_init_file(_init_files[idx])


func _load_init_file(path: String) -> void:
	_init_current_path = path
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		_init_text_edit.text = "(no se pudo abrir)"
		return
	_init_text_edit.text = f.get_as_text()
	f.close()
