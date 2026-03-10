## AnimationWindow.gd — Ventana de creacion de animacion por multi-seleccion
class_name AnimationWindow
extends Window

signal create_anim_pressed(indices: Array[int], speed: float)

var _frames: Array = []          # Array of frame dicts {sx, sy, w, h, grh_index, file_num}
var _sequence: Array[int] = []   # Ordered GRH indices for the animation
var _texture: ImageTexture = null
var _textures: Dictionary = {}

# UI
var _preview: FramePreviewPanel
var _seq_list: VBoxContainer
var _seq_scroll: ScrollContainer
var _speed_spin: SpinBox
var _btn_create: Button
var _lbl_info: Label

# Playback
var _playing: bool = true
var _play_time: float = 0.0
var _play_idx: int = 0


func _ready() -> void:
	title = "Animacion"
	size = Vector2i(500, 600)
	always_on_top = true
	unresizable = false
	wrap_controls = true
	close_requested.connect(func(): hide())

	var root := VBoxContainer.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_theme_constant_override("separation", 6)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.add_theme_constant_override("margin_top", 8)
	margin.add_theme_constant_override("margin_bottom", 8)
	margin.add_child(root)
	add_child(margin)

	# Preview area
	_preview = FramePreviewPanel.new()
	_preview.custom_minimum_size = Vector2(0, 200)
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root.add_child(_preview)

	# Info label
	_lbl_info = IndexerTheme.label("0 frames seleccionados", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_MD)
	root.add_child(_lbl_info)

	root.add_child(IndexerTheme.separator_h())

	# Sequence list (scrollable)
	_seq_scroll = ScrollContainer.new()
	_seq_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_seq_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	root.add_child(_seq_scroll)

	_seq_list = VBoxContainer.new()
	_seq_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_seq_list.add_theme_constant_override("separation", 2)
	_seq_scroll.add_child(_seq_list)

	root.add_child(IndexerTheme.separator_h())

	# Bottom bar: speed + create button
	var bottom := HBoxContainer.new()
	bottom.add_theme_constant_override("separation", 6)
	root.add_child(bottom)

	bottom.add_child(IndexerTheme.label("Velocidad", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))

	_speed_spin = IndexerTheme.spinbox(10, 99999, 500)
	_speed_spin.step = 10
	_speed_spin.suffix = " ms"
	_speed_spin.custom_minimum_size.x = 100
	bottom.add_child(_speed_spin)

	bottom.add_child(IndexerTheme.spacer())

	_btn_create = IndexerTheme.success_button("Crear GRH Animacion", _on_create)
	bottom.add_child(_btn_create)


func open_with_frames(frames: Array, texture: ImageTexture, textures: Dictionary) -> void:
	_frames = frames.duplicate()
	_texture = texture
	_textures = textures.duplicate()

	# Build initial sequence from frame grh_indices in order
	_sequence.clear()
	for f in _frames:
		var grh: int = f.get("grh_index", 0)
		if grh > 0:
			_sequence.append(grh)

	_play_idx = 0
	_play_time = 0.0
	_playing = true

	# Set texture on preview
	_preview.set_image(_texture.get_image() if _texture != null else null)
	if not _textures.is_empty():
		_preview.set_textures(_textures)

	_rebuild_list()
	_update_preview()

	popup_centered()


func _rebuild_list() -> void:
	for c in _seq_list.get_children():
		c.queue_free()

	_lbl_info.text = "%d frames seleccionados" % _sequence.size()
	_btn_create.disabled = _sequence.size() < 2

	for i in range(_sequence.size()):
		_build_row(i)


func _build_row(idx: int) -> void:
	var grh_idx: int = _sequence[idx]

	# Find the frame dict for this GRH
	var frame_dict: Dictionary = {}
	for f in _frames:
		if f.get("grh_index", 0) == grh_idx:
			frame_dict = f
			break

	var row_bg := StyleBoxFlat.new()
	row_bg.bg_color = IndexerTheme.BG_SECTION
	row_bg.set_corner_radius_all(3)
	row_bg.set_content_margin_all(3)

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", row_bg)
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 6)
	panel.add_child(hbox)

	# Thumbnail
	hbox.add_child(_make_thumb(frame_dict))

	# Label
	var w: int = frame_dict.get("w", 0)
	var h: int = frame_dict.get("h", 0)
	var lbl := IndexerTheme.label("GRH %d  —  %dx%d" % [grh_idx, w, h], IndexerTheme.TEXT_PRIMARY, IndexerTheme.FONT_SIZE_SM)
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(lbl)

	# Up button
	var btn_up := Button.new()
	btn_up.text = "▲"  # Unicode up arrow
	btn_up.custom_minimum_size = Vector2(28, 28)
	btn_up.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	btn_up.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 4, 2))
	btn_up.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 4, 2))
	btn_up.disabled = (idx == 0)
	var up_idx := idx
	btn_up.pressed.connect(func(): _on_move(up_idx, -1))
	hbox.add_child(btn_up)

	# Down button
	var btn_down := Button.new()
	btn_down.text = "▼"  # Unicode down arrow
	btn_down.custom_minimum_size = Vector2(28, 28)
	btn_down.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	btn_down.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 4, 2))
	btn_down.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 4, 2))
	btn_down.disabled = (idx == _sequence.size() - 1)
	var down_idx := idx
	btn_down.pressed.connect(func(): _on_move(down_idx, 1))
	hbox.add_child(btn_down)

	# Remove button
	var btn_remove := Button.new()
	btn_remove.text = "✕"
	btn_remove.custom_minimum_size = Vector2(28, 28)
	btn_remove.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	btn_remove.add_theme_color_override("font_color", IndexerTheme.TEXT_DANGER)
	btn_remove.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 4, 2))
	btn_remove.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 4, 2))
	var rem_idx := idx
	btn_remove.pressed.connect(func(): _on_remove(rem_idx))
	hbox.add_child(btn_remove)

	_seq_list.add_child(panel)


func _update_preview() -> void:
	# Build preview frames from sequence order
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


func _on_create() -> void:
	if _sequence.size() < 2:
		return
	var indices: Array[int] = []
	for grh in _sequence:
		indices.append(grh)
	create_anim_pressed.emit(indices, _speed_spin.value)
	hide()


func _make_thumb(f: Dictionary) -> TextureRect:
	var tr := TextureRect.new()
	tr.custom_minimum_size = Vector2(36, 36)
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
	if not visible:
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
