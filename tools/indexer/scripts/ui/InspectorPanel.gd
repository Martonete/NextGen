## InspectorPanel.gd — Right-side docked panel (3 tabs: Frames, Detección, Datos)
class_name InspectorPanel
extends VBoxContainer

# ── Signals (unchanged public API) ───────────────────────────────
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
signal index_frame_pressed(idx: int)
signal view_file_num_pressed(file_num: int, grh_index: int)
signal next_grh_changed(val: int)

var _tabs: TabContainer

# ── Frames tab ──
var _preview: FramePreviewPanel
var _btn_play: Button
var _lbl_anim_info: Label
var _spin_anim_fps: SpinBox
var _anim_playing: bool = false
var _anim_time: float = 0.0
var _anim_fps: float = 8.0
var _anim_frame_idx: int = 0
var _frames_for_anim: Array = []
var _fx_in_preview: bool = false  # True when main preview shows FX/body animation
var _body_in_preview: bool = false  # True when main preview shows body directions
var _body_anims: Array = []  # Array of body animation dicts (one per heading)
var _body_dir_tabs: HBoxContainer  # Norte/Este/Sur/Oeste tabs
var _body_current_dir: int = 0  # Currently selected direction index

var _props_popup: Window
var _props_box: VBoxContainer
var _spin_sx: SpinBox
var _spin_sy: SpinBox
var _spin_fw: SpinBox
var _spin_fh: SpinBox
var _spin_grh: SpinBox
var _lbl_prop_info: Label
var _props_updating: bool = false

var _spin_split_w: SpinBox
var _spin_split_h: SpinBox

var _frame_list_vbox: VBoxContainer
var _lbl_frame_count: Label
var _grh_entries: Dictionary = {}  # grh_index → entry (from Graficos.ind)
var _current_texture: ImageTexture = null
var _related_textures: Dictionary = {}  # file_num → ImageTexture (for remote frame thumbnails)
var _current_file_num: int = 0
var _related_frames: Array = []  # frames from related animations (other images)
var _confirm_index_idx: int = -1  # frame idx pending double-confirm
var _confirm_index_btn: Button = null  # button in confirm state

var _anim_section: VBoxContainer
var _anim_creator_win: Window
var _anim_avail_list: ItemList       # GRHs from current image
var _anim_seq_list: ItemList         # Animation sequence (ordered)
var _anim_seq_indices: Array[int] = []  # GRH indices in sequence
var _anim_manual_spin: SpinBox
var _anim_speed_spin: SpinBox
var _anim_avail_grhs: Array[int] = []   # GRH indices matching available list items

# ── Related animations section ──
var _related_anims_box: VBoxContainer
var _related_anims_content: VBoxContainer
var _related_previews: Array = []  # Array of FramePreviewPanel
var _related_anim_data: Array = []  # [{label, frames, playing, time, fps, frame_idx}]

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

# ── Data tab ──
var _spin_next_grh: SpinBox
var _spin_filenum: SpinBox
var _lbl_max_grh: Label
var _grh_viewer_vbox: VBoxContainer
var _lbl_grh_info: Label
var _init_file_list: ItemList
var _init_text_edit: TextEdit
var _init_files: Array[String] = []
var _init_current_path: String = ""


func _ready() -> void:
	custom_minimum_size.x = 380
	add_theme_constant_override("separation", 0)

	# Panel background
	var panel_bg := StyleBoxFlat.new()
	panel_bg.bg_color = IndexerTheme.BG_DARK
	panel_bg.content_margin_left = 2
	panel_bg.content_margin_right = 2

	_tabs = TabContainer.new()
	_tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_tabs.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	add_child(_tabs)

	_tabs.add_child(_build_frames_tab())
	_tabs.add_child(_build_detect_tab())
	_tabs.add_child(_build_data_tab())

	# Deferred: add popup to root window (must be done after entering tree)
	call_deferred("_attach_props_popup")


# ── Public API ────────────────────────────────────────────────────

func update_frames(frames: Array, selected: int) -> void:
	_rebuild_frame_list(frames, selected)
	if not _fx_in_preview:
		_frames_for_anim = frames
		_preview.set_frames(frames)
		if selected >= 0 and selected < frames.size():
			_preview.show_frame(selected)
			_show_props(true)
		else:
			_show_props(false)


func update_selected_props(frame: Dictionary) -> void:
	_props_updating = true
	_spin_sx.value = frame.get("sx", 0)
	_spin_sy.value = frame.get("sy", 0)
	_spin_fw.value = frame.get("w", 32)
	_spin_fh.value = frame.get("h", 32)
	_spin_grh.value = frame.get("grh_index", 1)
	_lbl_prop_info.text = "GRH %d  —  %dx%d px" % [frame.get("grh_index", 0), frame.get("w", 0), frame.get("h", 0)]
	_props_updating = false
	_show_props(true)


func clear_props() -> void:
	_lbl_prop_info.text = ""
	_show_props(false)


func set_image(img: Image) -> void:
	if _fx_in_preview:
		_exit_fx_preview()
	_preview.set_image(img)


func set_grh_entries(entries: Dictionary) -> void:
	_grh_entries = entries


func set_current_texture(tex: ImageTexture) -> void:
	_current_texture = tex


func set_current_file_num(fnum: int) -> void:
	_current_file_num = fnum


func set_related_frames(frames: Array) -> void:
	_related_frames = frames


func set_related_textures_for_list(textures: Dictionary) -> void:
	_related_textures = textures


func set_grh_data(max_idx: int, count: int) -> void:
	_lbl_max_grh.text = "Max GRH: %d  |  %d entradas" % [max_idx, count]
	_spin_next_grh.value = max_idx + 1


func get_next_grh() -> int:
	return int(_spin_next_grh.value)


func set_next_grh(val: int) -> void:
	_spin_next_grh.value = val


func get_filenum_base() -> int:
	return int(_spin_filenum.value)


func show_frame_preview(idx: int) -> void:
	if _fx_in_preview:
		_exit_fx_preview()
	_preview.show_frame(idx)
	_anim_frame_idx = idx


func _exit_fx_preview() -> void:
	_fx_in_preview = false
	_body_in_preview = false
	_anim_playing = false
	_btn_play.button_pressed = false
	_btn_play.text = "Play"
	_preview._textures.clear()
	_body_dir_tabs.visible = false
	_body_anims.clear()


func process_animation(delta: float) -> void:
	# Main preview animation
	if _anim_playing and _frames_for_anim.size() > 1:
		_anim_time += delta
		var dur := 1.0 / maxf(_anim_fps, 1.0)
		if _anim_time >= dur:
			_anim_time = fmod(_anim_time, dur)
			_anim_frame_idx = (_anim_frame_idx + 1) % _frames_for_anim.size()
			_preview.show_frame(_anim_frame_idx)
			_lbl_anim_info.text = "%d / %d" % [_anim_frame_idx + 1, _frames_for_anim.size()]

	# Related animation previews (bodies/fx headings)
	for i in range(_related_anim_data.size()):
		var ad: Dictionary = _related_anim_data[i]
		if not ad.get("playing", false):
			continue
		var frames: Array = ad.get("frames", [])
		if frames.size() <= 1:
			continue
		ad["time"] = ad.get("time", 0.0) + delta
		var spd: float = ad.get("fps", 8.0)
		var dur2 := 1.0 / maxf(spd, 1.0)
		if ad["time"] >= dur2:
			ad["time"] = fmod(ad["time"], dur2)
			ad["frame_idx"] = (ad.get("frame_idx", 0) + 1) % frames.size()
			if i < _related_previews.size():
				_related_previews[i].show_frame(ad["frame_idx"])


## Populate the "Related Animations" section.
## Each entry: {label: String, grh_index: int, frames: Array[Dict], speed: float, source: String}
## frames is Array of {sx, sy, w, h, grh_index, file_num} dicts (resolved statics).
func update_related_animations(anims: Array) -> void:
	# Clear old content
	for c in _related_anims_content.get_children():
		c.queue_free()
	_related_previews.clear()
	_related_anim_data.clear()
	_fx_in_preview = false
	_body_in_preview = false
	_body_anims.clear()
	_body_dir_tabs.visible = false

	if anims.is_empty():
		_related_anims_box.visible = false
		return

	# Check if we have a single FX — promote to main preview
	var fx_anims := anims.filter(func(a): return a.get("source", "") == "Fxs.ind")
	if fx_anims.size() == 1 and anims.size() == 1:
		_promote_fx_to_preview(fx_anims[0])
		return

	# Check if all are body animations — promote to main preview with direction tabs
	var body_anims := anims.filter(func(a): return a.get("source", "") == "Personajes.ind")
	if body_anims.size() == anims.size() and body_anims.size() > 0:
		_promote_body_to_preview(body_anims)
		return

	# Fallback: show each animation in its own preview (mixed FX + body or other)
	_related_anims_box.visible = true
	_build_related_anim_previews(anims)


func _promote_fx_to_preview(fx: Dictionary) -> void:
	var frames: Array = fx.get("frames", [])
	var speed: float = fx.get("speed", 100.0)
	var safe_speed := maxf(speed, 10.0)
	var fps := (frames.size() * 1000.0) / safe_speed

	_fx_in_preview = true
	_frames_for_anim = frames
	_preview.set_frames(frames)
	if not frames.is_empty():
		_preview.show_frame(0)
	_anim_fps = fps
	_anim_playing = true
	_anim_time = 0.0
	_anim_frame_idx = 0
	_btn_play.button_pressed = true
	_btn_play.text = "Pausa"
	_spin_anim_fps.value = roundf(fps)
	_lbl_anim_info.text = "FX %d — G%d — %d frames" % [fx.get("grh_index", 0), fx.get("grh_index", 0), frames.size()]

	_related_anims_box.visible = true
	var info_lbl := IndexerTheme.label(
		"FX %d en preview — G%d, %d frames, %.1f FPS" % [
			fx.get("grh_index", 0), fx.get("grh_index", 0), frames.size(), fps],
		IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	_related_anims_content.add_child(info_lbl)


func _promote_body_to_preview(anims: Array) -> void:
	_body_in_preview = true
	_fx_in_preview = true  # Reuse fx flag to prevent normal frame preview from overriding
	_body_anims = anims

	# Build per-direction data (fps, frames)
	# Anims may come in groups of 4 (N/E/S/W) per body. Take the first body's 4 directions.
	# Labels are "Body N — Norte", "Body N — Este", etc.
	var dir_data: Array = []
	for anim in anims:
		var frames: Array = anim.get("frames", [])
		var speed: float = anim.get("speed", 100.0)
		var safe_speed := maxf(speed, 10.0)
		var raw_fps := (frames.size() * 1000.0) / safe_speed
		var fps := raw_fps * 0.7  # Body walk 0.7x slowdown
		dir_data.append({
			"label": anim.get("label", ""),
			"grh_index": anim.get("grh_index", 0),
			"frames": frames,
			"fps": fps,
			"speed": speed
		})

	_body_anims = dir_data

	# Show direction tabs
	_body_dir_tabs.visible = true
	# Enable only available directions (some bodies may have < 4)
	for i in range(4):
		var btn: Button = _body_dir_tabs.get_child(i)
		if i < dir_data.size():
			btn.visible = true
			btn.disabled = false
		else:
			btn.visible = false

	# Select first direction
	_body_current_dir = 0
	_apply_body_direction(0)

	# Show info in related section
	_related_anims_box.visible = true
	var info_parts: PackedStringArray = []
	for d in dir_data:
		info_parts.append("G%d %df" % [d.grh_index, d.frames.size()])
	var info_lbl := IndexerTheme.label(
		"Body en preview — " + ", ".join(info_parts),
		IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	_related_anims_content.add_child(info_lbl)


func _on_body_dir_selected(dir_idx: int) -> void:
	if not _body_in_preview or dir_idx >= _body_anims.size():
		return
	_body_current_dir = dir_idx
	# Update tab toggle states
	for i in range(_body_dir_tabs.get_child_count()):
		var btn: Button = _body_dir_tabs.get_child(i)
		btn.set_pressed_no_signal(i == dir_idx)
	_apply_body_direction(dir_idx)


func _apply_body_direction(dir_idx: int) -> void:
	if dir_idx >= _body_anims.size():
		return
	var d: Dictionary = _body_anims[dir_idx]
	var frames: Array = d.frames
	var fps: float = d.fps

	_frames_for_anim = frames
	_preview.set_frames(frames)
	if not frames.is_empty():
		_preview.show_frame(0)
	_anim_fps = fps
	_anim_playing = true
	_anim_time = 0.0
	_anim_frame_idx = 0
	_btn_play.button_pressed = true
	_btn_play.text = "Pausa"
	_spin_anim_fps.value = roundf(fps)
	_lbl_anim_info.text = "%s — G%d — %d frames" % [d.label, d.grh_index, frames.size()]


func _build_related_anim_previews(anims: Array) -> void:
	for anim in anims:
		var label_text: String = anim.get("label", "Animación")
		var frames: Array = anim.get("frames", [])
		var speed: float = anim.get("speed", 8.0)
		var source: String = anim.get("source", "")
		var grh_idx: int = anim.get("grh_index", 0)

		var safe_speed := maxf(speed, 10.0)
		var raw_fps := (frames.size() * 1000.0) / safe_speed
		var anim_fps := raw_fps * 0.7 if source == "Personajes.ind" else raw_fps
		var ad := {
			"playing": true,
			"time": 0.0,
			"fps": anim_fps,
			"frame_idx": 0,
			"frames": frames
		}
		_related_anim_data.append(ad)

		var section := VBoxContainer.new()
		section.add_theme_constant_override("separation", 2)
		_related_anims_content.add_child(section)

		var header := HBoxContainer.new()
		header.add_theme_constant_override("separation", 4)
		section.add_child(header)
		header.add_child(IndexerTheme.label(label_text, IndexerTheme.TEXT_ACCENT, IndexerTheme.FONT_SIZE_SM))
		header.add_child(IndexerTheme.spacer())
		var info_text := "G%d  %df  spd=%.0f  %.1fFPS" % [grh_idx, frames.size(), speed, anim_fps]
		header.add_child(IndexerTheme.label(info_text, IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))

		var preview := FramePreviewPanel.new()
		preview.custom_minimum_size = Vector2(0, 120)
		preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		section.add_child(preview)

		preview.set_frames(frames)
		if not frames.is_empty():
			preview.show_frame(0)

		_related_previews.append(preview)


## Set the texture for related animation previews (called from Main after loading image)
func set_related_image(img: Image) -> void:
	for p in _related_previews:
		p.set_image(img)


func update_grh_viewer(entries: Array, texture: ImageTexture) -> void:
	# Legacy — now handled inline in the unified frame list.
	# Just update the texture reference for thumbnails.
	_current_texture = texture


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


func use_current_frames_for_anim(_frames: Array) -> void:
	pass  # Legacy — now handled by anim creator window


# ══════════════════════════════════════════════════════════════════
# TAB 1: FRAMES (preview + props + frame list + animation)
# ══════════════════════════════════════════════════════════════════

func _build_frames_tab() -> Control:
	var root := VBoxContainer.new()
	root.name = "Frames"
	root.add_theme_constant_override("separation", 0)

	# ── Preview area ──
	var preview_section := IndexerTheme.section_box(4)
	root.add_child(preview_section)
	var preview_vbox := VBoxContainer.new()
	preview_vbox.add_theme_constant_override("separation", 3)
	preview_section.add_child(preview_vbox)

	# Direction tabs (hidden by default, shown for body animations)
	_body_dir_tabs = HBoxContainer.new()
	_body_dir_tabs.add_theme_constant_override("separation", 2)
	_body_dir_tabs.visible = false
	preview_vbox.add_child(_body_dir_tabs)
	var dir_names := ["Norte", "Este", "Sur", "Oeste"]
	for di in range(4):
		var btn := Button.new()
		btn.text = dir_names[di]
		btn.toggle_mode = true
		btn.button_pressed = (di == 0)
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
		btn.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 6, 2))
		btn.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 6, 2))
		btn.add_theme_stylebox_override("pressed", IndexerTheme._flat_box(IndexerTheme.BG_TOOL_ACTIVE, 3, 6, 2))
		var d_idx := di
		btn.pressed.connect(func(): _on_body_dir_selected(d_idx))
		_body_dir_tabs.add_child(btn)

	_preview = FramePreviewPanel.new()
	_preview.custom_minimum_size = Vector2(0, 140)
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview_vbox.add_child(_preview)

	# Playback controls
	var playbar := HBoxContainer.new()
	playbar.add_theme_constant_override("separation", 3)
	preview_vbox.add_child(playbar)

	playbar.add_child(IndexerTheme.icon_button("<", func(): _anim_step(-1), "Frame anterior", 24))

	_btn_play = Button.new()
	_btn_play.text = "Play"
	_btn_play.toggle_mode = true
	_btn_play.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_btn_play.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 3, 8, 2))
	_btn_play.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 3, 8, 2))
	_btn_play.add_theme_stylebox_override("pressed", IndexerTheme._flat_box(IndexerTheme.BG_TOOL_ACTIVE, 3, 8, 2))
	_btn_play.toggled.connect(func(on: bool):
		_anim_playing = on
		_anim_time = 0.0
		_btn_play.text = "Pausa" if on else "Play")
	playbar.add_child(_btn_play)

	playbar.add_child(IndexerTheme.icon_button(">", func(): _anim_step(1), "Frame siguiente", 24))

	playbar.add_child(IndexerTheme.spacer())

	playbar.add_child(IndexerTheme.label("FPS", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_anim_fps = IndexerTheme.spinbox(1, 60, 8, func(v): _anim_fps = v)
	_spin_anim_fps.custom_minimum_size.x = 48
	playbar.add_child(_spin_anim_fps)

	_lbl_anim_info = IndexerTheme.label("--", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	playbar.add_child(_lbl_anim_info)

	# ── Properties popup (floats to the left of the inspector) ──
	_props_popup = Window.new()
	_props_popup.title = "Propiedades del frame"
	_props_popup.unresizable = true
	_props_popup.always_on_top = true
	_props_popup.transient = true
	_props_popup.exclusive = false
	_props_popup.visible = false
	_props_popup.wrap_controls = true
	_props_popup.close_requested.connect(func(): _props_popup.hide())
	# Dark background panel
	var popup_bg := PanelContainer.new()
	var popup_sb := StyleBoxFlat.new()
	popup_sb.bg_color = IndexerTheme.BG_PANEL
	popup_sb.border_color = IndexerTheme.ACCENT
	popup_sb.set_border_width_all(1)
	popup_sb.set_corner_radius_all(4)
	popup_sb.set_content_margin_all(8)
	popup_bg.add_theme_stylebox_override("panel", popup_sb)
	popup_bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_props_popup.add_child(popup_bg)

	_props_box = VBoxContainer.new()
	_props_box.add_theme_constant_override("separation", 4)
	popup_bg.add_child(_props_box)

	var props_header := HBoxContainer.new()
	props_header.add_theme_constant_override("separation", 6)
	_props_box.add_child(props_header)
	props_header.add_child(IndexerTheme.section_label("Propiedades"))
	props_header.add_child(IndexerTheme.spacer())
	_lbl_prop_info = IndexerTheme.label("", IndexerTheme.TEXT_ACCENT, IndexerTheme.FONT_SIZE_SM)
	props_header.add_child(_lbl_prop_info)

	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 4)
	grid.add_theme_constant_override("v_separation", 4)
	_props_box.add_child(grid)

	grid.add_child(IndexerTheme.label("GRH", IndexerTheme.TEXT_ACCENT, IndexerTheme.FONT_SIZE_SM))
	_spin_grh = IndexerTheme.spinbox(1, 999999, 1, func(_v): _on_props_changed())
	_spin_grh.custom_minimum_size.x = 72
	grid.add_child(_spin_grh)
	grid.add_child(Control.new())
	grid.add_child(Control.new())

	grid.add_child(IndexerTheme.label("X", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_sx = IndexerTheme.spinbox(0, 16383, 0, func(_v): _on_props_changed())
	_spin_sx.custom_minimum_size.x = 64
	grid.add_child(_spin_sx)
	grid.add_child(IndexerTheme.label("Y", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_sy = IndexerTheme.spinbox(0, 16383, 0, func(_v): _on_props_changed())
	_spin_sy.custom_minimum_size.x = 64
	grid.add_child(_spin_sy)

	grid.add_child(IndexerTheme.label("W", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_fw = IndexerTheme.spinbox(1, 16383, 32, func(_v): _on_props_changed())
	_spin_fw.custom_minimum_size.x = 64
	grid.add_child(_spin_fw)
	grid.add_child(IndexerTheme.label("H", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_fh = IndexerTheme.spinbox(1, 16383, 32, func(_v): _on_props_changed())
	_spin_fh.custom_minimum_size.x = 64
	grid.add_child(_spin_fh)

	# Action rows
	var action_row := HBoxContainer.new()
	action_row.add_theme_constant_override("separation", 4)
	_props_box.add_child(action_row)

	action_row.add_child(IndexerTheme.danger_button("Eliminar", func(): frame_deleted.emit(-1)))
	action_row.add_child(IndexerTheme.spacer())

	var split_row := HBoxContainer.new()
	split_row.add_theme_constant_override("separation", 4)
	_props_box.add_child(split_row)
	split_row.add_child(IndexerTheme.label("Dividir:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_split_w = IndexerTheme.spinbox(1, 2048, 32)
	_spin_split_w.custom_minimum_size.x = 52
	split_row.add_child(_spin_split_w)
	split_row.add_child(IndexerTheme.label("x", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_split_h = IndexerTheme.spinbox(1, 2048, 32)
	_spin_split_h.custom_minimum_size.x = 52
	split_row.add_child(_spin_split_h)
	split_row.add_child(IndexerTheme.button("Cortar", _on_split_btn, IndexerTheme.TEXT_ACCENT))

	# ── Resizable split: frame list (top) + related anims + anim creator (bottom) ──
	var split := VSplitContainer.new()
	split.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root.add_child(split)

	# ── Top: frame list ──
	var list_box := VBoxContainer.new()
	list_box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	split.add_child(list_box)

	var list_header := HBoxContainer.new()
	list_header.add_theme_constant_override("separation", 4)
	list_box.add_child(list_header)
	_lbl_frame_count = IndexerTheme.label("0 frames", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM)
	list_header.add_child(_lbl_frame_count)
	list_header.add_child(IndexerTheme.spacer())
	_lbl_grh_info = IndexerTheme.label("", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	list_header.add_child(_lbl_grh_info)
	list_header.add_child(IndexerTheme.button("+ Manual", func(): add_manual_frame_pressed.emit(), IndexerTheme.TEXT_ACCENT))
	list_header.add_child(IndexerTheme.danger_button("Limpiar", func(): clear_frames_pressed.emit()))

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	list_box.add_child(scroll)

	_frame_list_vbox = VBoxContainer.new()
	_frame_list_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_frame_list_vbox.add_theme_constant_override("separation", 1)
	scroll.add_child(_frame_list_vbox)

	# ── Bottom: related animations + animation creator (scrollable) ──
	var bottom_box := VBoxContainer.new()
	bottom_box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	bottom_box.custom_minimum_size.y = 80
	split.add_child(bottom_box)

	var bottom_scroll := ScrollContainer.new()
	bottom_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	bottom_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	bottom_box.add_child(bottom_scroll)

	var bottom_content := VBoxContainer.new()
	bottom_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom_content.add_theme_constant_override("separation", 4)
	bottom_scroll.add_child(bottom_content)

	# Related animations (bodies/fx detected from Personajes.ind/Fxs.ind)
	_related_anims_box = VBoxContainer.new()
	_related_anims_box.add_theme_constant_override("separation", 2)
	_related_anims_box.visible = false
	bottom_content.add_child(_related_anims_box)

	_related_anims_box.add_child(IndexerTheme.section_label("Animaciones relacionadas"))

	_related_anims_content = VBoxContainer.new()
	_related_anims_content.add_theme_constant_override("separation", 4)
	_related_anims_box.add_child(_related_anims_content)

	# Animation creator — just a button that opens the full dialog
	bottom_content.add_child(IndexerTheme.separator_h())

	_anim_section = VBoxContainer.new()
	_anim_section.add_theme_constant_override("separation", 3)
	bottom_content.add_child(_anim_section)

	_anim_section.add_child(IndexerTheme.primary_button("Crear GRH Animado...", _open_anim_creator))

	# Build the animation creator window (hidden until button pressed)
	_build_anim_creator_window()

	return root


# ══════════════════════════════════════════════════════════════════
# TAB 2: DETECCIÓN (grid + blobs)
# ══════════════════════════════════════════════════════════════════

func _build_detect_tab() -> Control:
	var root := VBoxContainer.new()
	root.name = "Detección"
	root.add_theme_constant_override("separation", 6)

	# ── Grid section ──
	root.add_child(IndexerTheme.section_label("Cuadrícula"))
	var grid_section := IndexerTheme.section_box(6)
	root.add_child(grid_section)
	var grid_inner := VBoxContainer.new()
	grid_inner.add_theme_constant_override("separation", 5)
	grid_section.add_child(grid_inner)

	var grid := GridContainer.new()
	grid.columns = 4
	grid.add_theme_constant_override("h_separation", 3)
	grid.add_theme_constant_override("v_separation", 3)
	grid_inner.add_child(grid)

	grid.add_child(IndexerTheme.label("W:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_cell_w = IndexerTheme.spinbox(1, 2048, 32)
	grid.add_child(_spin_cell_w)
	grid.add_child(IndexerTheme.label("H:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_cell_h = IndexerTheme.spinbox(1, 2048, 32)
	grid.add_child(_spin_cell_h)

	grid.add_child(IndexerTheme.label("OffX:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_off_x = IndexerTheme.spinbox(0, 2048, 0)
	grid.add_child(_spin_off_x)
	grid.add_child(IndexerTheme.label("OffY:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_off_y = IndexerTheme.spinbox(0, 2048, 0)
	grid.add_child(_spin_off_y)

	grid.add_child(IndexerTheme.label("MrgX:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_mrg_x = IndexerTheme.spinbox(0, 512, 0)
	grid.add_child(_spin_mrg_x)
	grid.add_child(IndexerTheme.label("MrgY:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_spin_mrg_y = IndexerTheme.spinbox(0, 512, 0)
	grid.add_child(_spin_mrg_y)

	_chk_skip_empty = CheckButton.new()
	_chk_skip_empty.text = "Saltar celdas vacías"
	_chk_skip_empty.button_pressed = true
	_chk_skip_empty.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	grid_inner.add_child(_chk_skip_empty)

	# Presets
	grid_inner.add_child(IndexerTheme.label("Presets:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	var presets_row1 := HBoxContainer.new()
	presets_row1.add_theme_constant_override("separation", 3)
	grid_inner.add_child(presets_row1)
	presets_row1.add_child(IndexerTheme.preset_button("Cuerpo 25x45", _make_preset_cb(25, 45), "Cuerpos, armas, movimientos"))
	presets_row1.add_child(IndexerTheme.preset_button("Cabeza 16x16", _make_preset_cb(16, 16), "Cabezas"))
	presets_row1.add_child(IndexerTheme.preset_button("Escudo 25x25", _make_preset_cb(25, 25), "Escudos"))

	var presets_row2 := HBoxContainer.new()
	presets_row2.add_theme_constant_override("separation", 3)
	grid_inner.add_child(presets_row2)
	for p in [[32,32],[64,64],[128,128],[256,256]]:
		var pw: int = p[0]; var ph: int = p[1]
		presets_row2.add_child(IndexerTheme.preset_button("%dx%d" % [pw, ph], _make_preset_cb(pw, ph), "Tiles %dx%d" % [pw, ph]))

	grid_inner.add_child(IndexerTheme.primary_button("Detectar Grid", _on_detect_grid_btn))

	# ── Blob section ──
	root.add_child(IndexerTheme.separator_h())
	root.add_child(IndexerTheme.section_label("Blobs (alfa)"))

	var blob_section := IndexerTheme.section_box(6)
	root.add_child(blob_section)
	var blob_inner := VBoxContainer.new()
	blob_inner.add_theme_constant_override("separation", 5)
	blob_section.add_child(blob_inner)

	var bgrid := GridContainer.new()
	bgrid.columns = 4
	bgrid.add_theme_constant_override("h_separation", 3)
	bgrid.add_theme_constant_override("v_separation", 3)
	blob_inner.add_child(bgrid)

	bgrid.add_child(IndexerTheme.label("Alpha%:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_alpha = IndexerTheme.spinbox(0, 100, 3)
	bgrid.add_child(_spin_alpha)
	bgrid.add_child(IndexerTheme.label("MinPx:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_min_size = IndexerTheme.spinbox(1, 512, 4)
	bgrid.add_child(_spin_min_size)
	bgrid.add_child(IndexerTheme.label("Pad:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_padding = IndexerTheme.spinbox(0, 64, 1)
	bgrid.add_child(_spin_padding)
	bgrid.add_child(Control.new())
	bgrid.add_child(Control.new())

	blob_inner.add_child(IndexerTheme.primary_button("Detectar Blobs", _on_detect_blobs_btn))

	return root


# ══════════════════════════════════════════════════════════════════
# TAB 3: DATOS (config + GRH viewer + INIT editor)
# ══════════════════════════════════════════════════════════════════

func _build_data_tab() -> Control:
	var root := VBoxContainer.new()
	root.name = "Datos"
	root.add_theme_constant_override("separation", 6)

	# ── Config section ──
	root.add_child(IndexerTheme.section_label("Configuración"))
	var config_section := IndexerTheme.section_box(5)
	root.add_child(config_section)
	var config_inner := VBoxContainer.new()
	config_inner.add_theme_constant_override("separation", 4)
	config_section.add_child(config_inner)

	_lbl_max_grh = IndexerTheme.label("Max GRH: --", IndexerTheme.TEXT_SUCCESS, IndexerTheme.FONT_SIZE_MD)
	config_inner.add_child(_lbl_max_grh)

	var config_grid := GridContainer.new()
	config_grid.columns = 2
	config_grid.add_theme_constant_override("h_separation", 8)
	config_grid.add_theme_constant_override("v_separation", 4)
	config_inner.add_child(config_grid)

	config_grid.add_child(IndexerTheme.label("Próximo GRH:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_next_grh = IndexerTheme.spinbox(1, 999999, 1, func(v): next_grh_changed.emit(int(v)))
	config_grid.add_child(_spin_next_grh)

	config_grid.add_child(IndexerTheme.label("FileNum base:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_spin_filenum = IndexerTheme.spinbox(1, 99999, 1)
	config_grid.add_child(_spin_filenum)

	# ── INIT file editor ──
	root.add_child(IndexerTheme.separator_h())
	root.add_child(IndexerTheme.section_label("Archivos INIT"))

	var init_split := VSplitContainer.new()
	init_split.size_flags_vertical = Control.SIZE_EXPAND_FILL
	init_split.split_offset = 100
	root.add_child(init_split)

	var init_top := VBoxContainer.new()
	init_top.add_theme_constant_override("separation", 3)
	init_split.add_child(init_top)

	_init_file_list = ItemList.new()
	_init_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_file_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_init_file_list.item_selected.connect(_on_init_file_selected)
	init_top.add_child(_init_file_list)

	var init_btn_row := HBoxContainer.new()
	init_btn_row.add_theme_constant_override("separation", 4)
	init_top.add_child(init_btn_row)
	init_btn_row.add_child(IndexerTheme.success_button("Guardar", _on_save_init_btn))
	init_btn_row.add_child(IndexerTheme.button("Recargar", func():
		if not _init_current_path.is_empty():
			_load_init_file(_init_current_path)))

	_init_text_edit = TextEdit.new()
	_init_text_edit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_text_edit.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_init_text_edit.placeholder_text = "Selecciona un archivo INIT"
	init_split.add_child(_init_text_edit)

	return root


# ── Internal helpers ──────────────────────────────────────────────

func _attach_props_popup() -> void:
	if _props_popup == null or _props_popup.is_inside_tree():
		return
	get_tree().root.add_child(_props_popup)
	_props_popup.visible = false


func _show_props(vis: bool) -> void:
	if _props_popup == null or not _props_popup.is_inside_tree():
		return
	if not vis:
		_props_popup.visible = false
		return
	# Position to the left of the inspector panel
	var inspector_rect := get_global_rect()
	var popup_w := 280
	var popup_h := 240
	var px := int(inspector_rect.position.x) - popup_w - 8
	var py := int(inspector_rect.position.y) + 40
	# Clamp to screen
	if px < 4:
		px = 4
	_props_popup.position = Vector2i(px, py)
	_props_popup.size = Vector2i(popup_w, popup_h)
	_props_popup.visible = true


func _rebuild_frame_list(frames: Array, selected: int) -> void:
	for c in _frame_list_vbox.get_children():
		c.free()
	_confirm_index_idx = -1
	_confirm_index_btn = null

	# Build combined list: local frames + related remote frames
	# Each entry: {frame, local_idx (or -1), is_local, file_num}
	var combined: Array = []
	for i in range(frames.size()):
		combined.append({
			"frame": frames[i],
			"local_idx": i,
			"is_local": true,
			"file_num": frames[i].get("file_num", _current_file_num)
		})

	# Add related frames from other images (skip those already in local frames)
	var local_grhs: Dictionary = {}
	for f in frames:
		local_grhs[f.get("grh_index", 0)] = true
	for rf in _related_frames:
		var rf_fnum: int = rf.get("file_num", 0)
		var rf_grh: int = rf.get("grh_index", 0)
		if rf_fnum != _current_file_num and not local_grhs.has(rf_grh):
			combined.append({
				"frame": rf,
				"local_idx": -1,
				"is_local": false,
				"file_num": rf_fnum
			})
			local_grhs[rf_grh] = true  # Prevent dupes

	# Count stats
	var indexed_count := 0
	var local_count := 0
	for entry in combined:
		var grh_idx: int = entry.frame.get("grh_index", 0)
		if _grh_entries.has(grh_idx):
			indexed_count += 1
		if entry.is_local:
			local_count += 1
	_lbl_frame_count.text = "%d frames" % local_count
	var extra := combined.size() - local_count
	var info_parts: PackedStringArray = []
	if indexed_count > 0:
		info_parts.append("%d indexados" % indexed_count)
	if extra > 0:
		info_parts.append("+%d relacionados" % extra)
	_lbl_grh_info.text = "  ".join(info_parts)

	for ci in range(combined.size()):
		var entry: Dictionary = combined[ci]
		var f: Dictionary = entry.frame
		var is_local: bool = entry.is_local
		var local_idx: int = entry.local_idx
		var fnum: int = entry.file_num
		var is_sel := is_local and (local_idx == selected)
		var grh_idx: int = f.get("grh_index", 0)
		var is_indexed: bool = _grh_entries.has(grh_idx)
		var is_remote := not is_local

		# Row background
		var row_bg := StyleBoxFlat.new()
		if is_sel:
			row_bg.bg_color = IndexerTheme.BG_SELECTED
		elif is_remote:
			row_bg.bg_color = Color(0.15, 0.13, 0.18)  # Subtle purple tint for remote
		else:
			row_bg.bg_color = Color.TRANSPARENT
		row_bg.set_corner_radius_all(3)
		row_bg.content_margin_left = 3
		row_bg.content_margin_right = 3
		row_bg.content_margin_top = 2
		row_bg.content_margin_bottom = 2

		var panel := PanelContainer.new()
		panel.add_theme_stylebox_override("panel", row_bg)
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL

		var outer := HBoxContainer.new()
		outer.add_theme_constant_override("separation", 4)
		panel.add_child(outer)

		# Thumbnail (larger: 36x36)
		var tex: ImageTexture = _current_texture if is_local else _related_textures.get(fnum, null)
		if tex != null:
			var preview_rect := TextureRect.new()
			var atlas := AtlasTexture.new()
			atlas.atlas = tex
			atlas.region = Rect2(f.get("sx", 0), f.get("sy", 0), f.get("w", 32), f.get("h", 32))
			preview_rect.texture = atlas
			preview_rect.expand_mode = TextureRect.EXPAND_FIT_WIDTH_PROPORTIONAL
			preview_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			preview_rect.custom_minimum_size = Vector2(36, 36)
			outer.add_child(preview_rect)

		# Two-line info: line1 = filename + GRH, line2 = offsets + size
		var info_col := VBoxContainer.new()
		info_col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		info_col.add_theme_constant_override("separation", 0)
		outer.add_child(info_col)

		# Line 1: (filename.png) — GRH<num>
		var line1 := HBoxContainer.new()
		line1.add_theme_constant_override("separation", 4)
		info_col.add_child(line1)

		var fname_text := "(%d.png)" % fnum
		var fname_col := IndexerTheme.TEXT_WARNING if is_remote else (Color.WHITE if is_sel else IndexerTheme.TEXT_SECONDARY)
		line1.add_child(IndexerTheme.label(fname_text, fname_col, IndexerTheme.FONT_SIZE_SM))

		var grh_text := "GRH %d" % grh_idx
		var grh_col := Color.WHITE if is_sel else IndexerTheme.TEXT_PRIMARY
		var grh_lbl := IndexerTheme.label(grh_text, grh_col, IndexerTheme.FONT_SIZE_MD)
		grh_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		line1.add_child(grh_lbl)

		# Indexed badge on line 1
		if is_indexed:
			line1.add_child(IndexerTheme.label("INDEXADO", IndexerTheme.TEXT_SUCCESS, 9))
		elif is_local:
			var btn_idx := Button.new()
			btn_idx.text = "+"
			btn_idx.tooltip_text = "Agregar a Graficos.ind (doble click)"
			btn_idx.custom_minimum_size.x = 22
			btn_idx.add_theme_font_size_override("font_size", 9)
			btn_idx.add_theme_color_override("font_color", IndexerTheme.TEXT_WARNING)
			btn_idx.add_theme_stylebox_override("normal", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST, 2, 2, 1))
			btn_idx.add_theme_stylebox_override("hover", IndexerTheme._flat_box(IndexerTheme.BG_BTN_GHOST_H, 2, 2, 1))
			btn_idx.pressed.connect(_on_index_frame_btn.bind(local_idx, btn_idx))
			line1.add_child(btn_idx)

		# Line 2: offsets + size
		var line2_text := "pos (%d, %d)  —  %d x %d px" % [f.get("sx", 0), f.get("sy", 0), f.get("w", 0), f.get("h", 0)]
		var line2_col := Color(0.75, 0.75, 0.78) if is_sel else IndexerTheme.TEXT_MUTED
		info_col.add_child(IndexerTheme.label(line2_text, line2_col, IndexerTheme.FONT_SIZE_SM))

		# Action buttons
		var btn_col := VBoxContainer.new()
		btn_col.add_theme_constant_override("separation", 1)
		outer.add_child(btn_col)

		if is_remote:
			# "Ver" button — navigates to that graphic (deferred to avoid freeing self)
			var r_fnum := fnum
			var r_grh := grh_idx
			var btn_ver := IndexerTheme.icon_button("Ver", func(): call_deferred("emit_signal", "view_file_num_pressed", r_fnum, r_grh), "Abrir gráfico %d" % fnum, 32)
			btn_ver.add_theme_font_size_override("font_size", 9)
			btn_ver.add_theme_color_override("font_color", IndexerTheme.TEXT_ACCENT)
			btn_col.add_child(btn_ver)
		elif not is_sel:
			# Deferred to avoid freeing the button while its callback runs
			var l_idx := local_idx
			var btn_sel := IndexerTheme.icon_button("Sel", func(): call_deferred("emit_signal", "frame_selected", l_idx), "Seleccionar", 32)
			btn_sel.add_theme_font_size_override("font_size", 9)
			btn_col.add_child(btn_sel)

		if is_local:
			var l_idx2 := local_idx
			var btn_del := IndexerTheme.icon_button("X", func(): call_deferred("emit_signal", "frame_deleted", l_idx2), "Eliminar", 22)
			btn_del.add_theme_font_size_override("font_size", 9)
			btn_del.add_theme_color_override("font_color", IndexerTheme.TEXT_DANGER)
			btn_col.add_child(btn_del)

		_frame_list_vbox.add_child(panel)


func _on_index_frame_btn(idx: int, btn: Button) -> void:
	if _confirm_index_idx == idx and _confirm_index_btn == btn:
		# Second click — confirm
		_confirm_index_idx = -1
		_confirm_index_btn = null
		index_frame_pressed.emit(idx)
	else:
		# First click — enter confirm state, reset previous if any
		if _confirm_index_btn != null and is_instance_valid(_confirm_index_btn):
			_confirm_index_btn.text = "+"
			_confirm_index_btn.add_theme_color_override("font_color", IndexerTheme.TEXT_WARNING)
		_confirm_index_idx = idx
		_confirm_index_btn = btn
		btn.text = "OK?"
		btn.add_theme_color_override("font_color", IndexerTheme.TEXT_DANGER)


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


func _build_anim_creator_window() -> void:
	_anim_creator_win = Window.new()
	_anim_creator_win.title = "Crear GRH Animado"
	_anim_creator_win.unresizable = false
	_anim_creator_win.always_on_top = true
	_anim_creator_win.transient = true
	_anim_creator_win.exclusive = false
	_anim_creator_win.visible = false
	_anim_creator_win.wrap_controls = true
	_anim_creator_win.size = Vector2i(480, 420)
	_anim_creator_win.close_requested.connect(func(): _anim_creator_win.hide())

	var bg := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = IndexerTheme.BG_PANEL
	sb.set_content_margin_all(8)
	bg.add_theme_stylebox_override("panel", sb)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_anim_creator_win.add_child(bg)

	var main_vbox := VBoxContainer.new()
	main_vbox.add_theme_constant_override("separation", 6)
	bg.add_child(main_vbox)

	# ── Top: two columns (available | sequence) ──
	var hsplit := HSplitContainer.new()
	hsplit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	main_vbox.add_child(hsplit)

	# Left: Available GRHs from current image
	var left := VBoxContainer.new()
	left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	left.add_theme_constant_override("separation", 2)
	hsplit.add_child(left)

	left.add_child(IndexerTheme.label("GRHs disponibles", IndexerTheme.TEXT_ACCENT, IndexerTheme.FONT_SIZE_MD))
	left.add_child(IndexerTheme.label("Click para agregar", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))

	_anim_avail_list = ItemList.new()
	_anim_avail_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_anim_avail_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_anim_avail_list.item_activated.connect(_on_anim_avail_activated)
	left.add_child(_anim_avail_list)

	# Right: Animation sequence
	var right := VBoxContainer.new()
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	right.add_theme_constant_override("separation", 2)
	hsplit.add_child(right)

	right.add_child(IndexerTheme.label("Secuencia", IndexerTheme.TEXT_SUCCESS, IndexerTheme.FONT_SIZE_MD))

	var seq_btns := HBoxContainer.new()
	seq_btns.add_theme_constant_override("separation", 3)
	right.add_child(seq_btns)
	seq_btns.add_child(IndexerTheme.icon_button("^", _on_anim_seq_move_up, "Subir", 24))
	seq_btns.add_child(IndexerTheme.icon_button("v", _on_anim_seq_move_down, "Bajar", 24))
	seq_btns.add_child(IndexerTheme.spacer())
	seq_btns.add_child(IndexerTheme.danger_button("Quitar", _on_anim_seq_remove))
	seq_btns.add_child(IndexerTheme.danger_button("Limpiar", _on_anim_seq_clear))

	_anim_seq_list = ItemList.new()
	_anim_seq_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_anim_seq_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	right.add_child(_anim_seq_list)

	# ── Bottom: manual add + speed + save ──
	var bottom := VBoxContainer.new()
	bottom.add_theme_constant_override("separation", 4)
	main_vbox.add_child(bottom)

	# Manual GRH add row
	var manual_row := HBoxContainer.new()
	manual_row.add_theme_constant_override("separation", 4)
	bottom.add_child(manual_row)
	manual_row.add_child(IndexerTheme.label("GRH manual:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_anim_manual_spin = IndexerTheme.spinbox(1, 999999, 1)
	_anim_manual_spin.custom_minimum_size.x = 80
	manual_row.add_child(_anim_manual_spin)
	manual_row.add_child(IndexerTheme.button("Agregar", _on_anim_manual_add, IndexerTheme.TEXT_ACCENT))
	manual_row.add_child(IndexerTheme.spacer())

	# Speed + Save row
	var save_row := HBoxContainer.new()
	save_row.add_theme_constant_override("separation", 4)
	bottom.add_child(save_row)
	save_row.add_child(IndexerTheme.label("Speed (ms):", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM))
	_anim_speed_spin = IndexerTheme.spinbox(10, 99999, 500)
	_anim_speed_spin.step = 10
	_anim_speed_spin.custom_minimum_size.x = 80
	save_row.add_child(_anim_speed_spin)
	save_row.add_child(IndexerTheme.spacer())
	save_row.add_child(IndexerTheme.success_button("Guardar en GrhIndex", _on_anim_save))


func _open_anim_creator() -> void:
	if _anim_creator_win == null:
		return
	# Attach to tree if needed
	if not _anim_creator_win.is_inside_tree():
		get_tree().root.add_child(_anim_creator_win)

	# Populate available GRHs from current image's frames
	_anim_avail_list.clear()
	_anim_avail_grhs.clear()
	for f in _frames_for_anim:
		var grh_idx: int = f.get("grh_index", 0)
		if grh_idx > 0:
			_anim_avail_grhs.append(grh_idx)
			var w: int = f.get("w", 0)
			var h: int = f.get("h", 0)
			_anim_avail_list.add_item("GRH %d  (%dx%d)" % [grh_idx, w, h])

	# Clear sequence
	_anim_seq_indices.clear()
	_anim_seq_list.clear()

	# Position and show
	var inspector_rect := get_global_rect()
	var wx := int(inspector_rect.position.x) - 490
	if wx < 4:
		wx = 4
	var wy := int(inspector_rect.position.y) + 20
	_anim_creator_win.position = Vector2i(wx, wy)
	_anim_creator_win.visible = true


func _on_anim_avail_activated(idx: int) -> void:
	if idx < 0 or idx >= _anim_avail_grhs.size():
		return
	var grh_idx: int = _anim_avail_grhs[idx]
	_anim_seq_indices.append(grh_idx)
	_anim_seq_list.add_item("GRH %d" % grh_idx)


func _on_anim_manual_add() -> void:
	var grh_idx := int(_anim_manual_spin.value)
	if grh_idx > 0:
		_anim_seq_indices.append(grh_idx)
		_anim_seq_list.add_item("GRH %d (manual)" % grh_idx)


func _on_anim_seq_remove() -> void:
	var sel := _anim_seq_list.get_selected_items()
	if sel.is_empty():
		return
	var idx: int = sel[0]
	_anim_seq_indices.remove_at(idx)
	_anim_seq_list.remove_item(idx)


func _on_anim_seq_clear() -> void:
	_anim_seq_indices.clear()
	_anim_seq_list.clear()


func _on_anim_seq_move_up() -> void:
	var sel := _anim_seq_list.get_selected_items()
	if sel.is_empty() or sel[0] <= 0:
		return
	var idx: int = sel[0]
	# Swap in data
	var tmp: int = _anim_seq_indices[idx]
	_anim_seq_indices[idx] = _anim_seq_indices[idx - 1]
	_anim_seq_indices[idx - 1] = tmp
	# Swap in list
	var text_a := _anim_seq_list.get_item_text(idx)
	var text_b := _anim_seq_list.get_item_text(idx - 1)
	_anim_seq_list.set_item_text(idx, text_b)
	_anim_seq_list.set_item_text(idx - 1, text_a)
	_anim_seq_list.select(idx - 1)


func _on_anim_seq_move_down() -> void:
	var sel := _anim_seq_list.get_selected_items()
	if sel.is_empty() or sel[0] >= _anim_seq_indices.size() - 1:
		return
	var idx: int = sel[0]
	var tmp: int = _anim_seq_indices[idx]
	_anim_seq_indices[idx] = _anim_seq_indices[idx + 1]
	_anim_seq_indices[idx + 1] = tmp
	var text_a := _anim_seq_list.get_item_text(idx)
	var text_b := _anim_seq_list.get_item_text(idx + 1)
	_anim_seq_list.set_item_text(idx, text_b)
	_anim_seq_list.set_item_text(idx + 1, text_a)
	_anim_seq_list.select(idx + 1)


func _on_anim_save() -> void:
	if _anim_seq_indices.size() < 2:
		return
	create_anim_pressed.emit(_anim_seq_indices.duplicate(), _anim_speed_spin.value)
	_anim_creator_win.hide()


func _on_props_changed() -> void:
	if _props_updating:
		return
	frame_props_changed.emit(-1, int(_spin_sx.value), int(_spin_sy.value), int(_spin_fw.value), int(_spin_fh.value), int(_spin_grh.value))


func _anim_step(dir: int) -> void:
	if _frames_for_anim.is_empty():
		return
	_anim_frame_idx = clampi(_anim_frame_idx + dir, 0, _frames_for_anim.size() - 1)
	_preview.show_frame(_anim_frame_idx)
	_lbl_anim_info.text = "%d / %d" % [_anim_frame_idx + 1, _frames_for_anim.size()]


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
