## Main.gd — Argentum Sprite Indexer (v2 — docked panels)
## Orchestrates all UI components: FileListPanel, ToolBar, InspectorPanel, SpriteCanvas.

extends Control

# ── Core state ───────────────────────────────────────────────────────────────

var _grh_data: Dictionary = { "version": 12, "max_index": 0, "entries": {} }
var _ind_path: String = ""

var _current_image_path: String = ""
var _current_image: Image = null
var _current_texture: ImageTexture = null
var _current_file_num: int = 0
var _current_frames: Array = []
var _selected_frame_idx: int = -1
var _next_grh_index: int = 1
var _filenum_base: int = 1

# Client folder mode
var _using_client: bool = false
var _graficos_folder_path: String = ""
var _client_graficos_path: String = ""

# INIT data
var _init_folder: String = ""
var _bodies_data: Array = []
var _fxs_data: Array = []
var _mi_cabecera_fxs: PackedByteArray = PackedByteArray()
var _indices_ini_data: PackedStringArray = []  # Raw lines of indices.ini
var _indices_categories: PackedStringArray = []  # Unique category names
var _dirty: bool = false  # True when there are unsaved changes in memory

# Undo stack: stores snapshots of {frames, selected, next_grh}
const MAX_UNDO := 50
var _undo_stack: Array = []
var _redo_stack: Array = []

# Background analysis thread
var _analysis_thread: Thread = null
var _analysis_result: Dictionary = {}
var _analysis_pending: bool = false
var _loading_overlay: PanelContainer = null
var _loading_label: Label = null

# ── UI components ────────────────────────────────────────────────────────────

var _toolbar: IndexerToolBar
var _file_list: FileListPanel
var _canvas: SpriteCanvas
var _inspector: InspectorPanel
var _lbl_status: Label
var _menu_ver: PopupMenu

# Context menu & texture indexing
var _ctx_menu: PopupMenu
var _ctx_frame_idx: int = -1
var _tex_index_dialog: TextureIndexDialog
var _dlg_save_confirm: Window = null
var _index_preview_label_save: RichTextLabel = null

# Dialogs
var _dlg_client_folder: FileDialog
var _dlg_save_ind: FileDialog
var _dlg_index_confirm: Window
var _index_preview_label: RichTextLabel
var _index_pending_entries: Array = []

# Prefs
const PREFS_PATH := "user://indexer_prefs.cfg"
const MAX_RECENT := 8
var _prefs: ConfigFile
var _recent_clients: Array = []


# ── Init ─────────────────────────────────────────────────────────────────────

func _ready() -> void:
	_prefs = ConfigFile.new()
	_load_prefs()
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_MAXIMIZED)
	_build_ui()
	_build_dialogs()
	_connect_signals()
	# Default: detection off, grid visible
	_canvas.set_detect(false)
	_canvas.set_grid_visible(true)
	_update_status("Listo. Abre una carpeta de cliente para comenzar.")
	# Restore previous session
	call_deferred("_restore_session")


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_finish_analysis_thread()
		_save_session()
		get_tree().quit()


func _process(delta: float) -> void:
	# Check if background analysis finished
	if _analysis_pending and _analysis_thread != null and not _analysis_thread.is_alive():
		var result: Dictionary = _analysis_thread.wait_to_finish()
		_analysis_thread = null
		_analysis_pending = false
		_hide_loading()
		if result.has("blob_data"):
			_apply_analysis_results(
				result["blob_data"], result["content_regions"],
				result["rects"], result["file_num"])

	# Thumbnail lazy-loading
	_file_list.process_thumbnails(5)
	# Animation playback
	_inspector.process_animation(delta)


# ── UI Construction ──────────────────────────────────────────────────────────

func _build_ui() -> void:
	# Dark background
	var bg := StyleBoxFlat.new()
	bg.bg_color = IndexerTheme.BG_DARK
	add_theme_stylebox_override("panel", bg)

	var root := VBoxContainer.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_theme_constant_override("separation", 0)
	add_child(root)

	# Menu bar
	root.add_child(_build_menu_bar())

	# Toolbar
	_toolbar = IndexerToolBar.new()
	root.add_child(_toolbar)

	# Main body: [FileList | Canvas | Inspector]
	var hsplit_outer := HSplitContainer.new()
	hsplit_outer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	hsplit_outer.split_offset = 220
	root.add_child(hsplit_outer)

	# Left: file list
	_file_list = FileListPanel.new()
	hsplit_outer.add_child(_file_list)

	# Right split: [Canvas | Inspector]
	var hsplit_inner := HSplitContainer.new()
	hsplit_inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hsplit_inner.split_offset = -380
	hsplit_outer.add_child(hsplit_inner)

	# Canvas (center, fills available space)
	_canvas = SpriteCanvas.new()
	_canvas.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_canvas.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_canvas.focus_mode = Control.FOCUS_CLICK
	_canvas.clip_contents = true
	hsplit_inner.add_child(_canvas)

	# Inspector (right)
	_inspector = InspectorPanel.new()
	hsplit_inner.add_child(_inspector)

	# Status bar
	var status_bar := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = IndexerTheme.BG_PANEL
	sb.set_content_margin_all(2)
	status_bar.add_theme_stylebox_override("panel", sb)
	_lbl_status = IndexerTheme.label("", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	status_bar.add_child(_lbl_status)
	root.add_child(status_bar)

	# Loading overlay (covers entire window, hidden by default)
	_loading_overlay = PanelContainer.new()
	var overlay_sb := StyleBoxFlat.new()
	overlay_sb.bg_color = Color(0.0, 0.0, 0.0, 0.65)
	_loading_overlay.add_theme_stylebox_override("panel", overlay_sb)
	_loading_overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	_loading_overlay.mouse_filter = Control.MOUSE_FILTER_STOP  # Block clicks
	_loading_overlay.visible = false
	add_child(_loading_overlay)

	var overlay_center := CenterContainer.new()
	overlay_center.set_anchors_preset(Control.PRESET_FULL_RECT)
	_loading_overlay.add_child(overlay_center)

	var overlay_box := VBoxContainer.new()
	overlay_box.add_theme_constant_override("separation", 12)
	overlay_center.add_child(overlay_box)

	_loading_label = Label.new()
	_loading_label.text = "Analizando imagen..."
	_loading_label.add_theme_font_size_override("font_size", 20)
	_loading_label.add_theme_color_override("font_color", Color.WHITE)
	_loading_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	overlay_box.add_child(_loading_label)

	# Context menu for right-click on frame
	_ctx_menu = PopupMenu.new()
	_ctx_menu.add_item("Indexar como textura", 0)
	_ctx_menu.id_pressed.connect(_on_ctx_menu_item)
	add_child(_ctx_menu)

	# Texture indexing dialog (not added as child yet — lazy add on popup)
	_tex_index_dialog = TextureIndexDialog.new()
	_tex_index_dialog.confirmed.connect(_on_texture_index_confirmed)
	_tex_index_dialog.split_requested.connect(_on_texture_split_requested)

	# Confirmation dialog — created lazily in _ensure_confirm_dialog()


func _build_menu_bar() -> MenuBar:
	var mb := MenuBar.new()
	mb.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)

	var m_archivo := PopupMenu.new()
	m_archivo.name = "Archivo"
	m_archivo.add_item("Abrir carpeta Cliente...", 0, KEY_MASK_CTRL | KEY_O)
	m_archivo.add_separator()
	m_archivo.add_item("Salir", 99, KEY_MASK_CTRL | KEY_Q)
	m_archivo.id_pressed.connect(_on_archivo_menu)
	mb.add_child(m_archivo)

	var m_editar := PopupMenu.new()
	m_editar.name = "Editar"
	m_editar.add_item("Deshacer", 0, KEY_MASK_CTRL | KEY_Z)
	m_editar.add_item("Rehacer", 1, KEY_MASK_CTRL | KEY_Y)
	m_editar.add_separator()
	m_editar.add_item("Limpiar frames", 2)
	m_editar.id_pressed.connect(_on_editar_menu)
	mb.add_child(m_editar)

	var m_guardar := PopupMenu.new()
	m_guardar.name = "Guardar"
	m_guardar.add_item("Guardar Graficos.ind", 0, KEY_MASK_CTRL | KEY_S)
	m_guardar.add_item("Guardar Personajes.ind", 1)
	m_guardar.add_item("Guardar Fxs.ind", 2)
	m_guardar.add_separator()
	m_guardar.add_item("Guardar como...", 3, KEY_MASK_CTRL | KEY_MASK_SHIFT | KEY_S)
	m_guardar.id_pressed.connect(_on_guardar_menu)
	mb.add_child(m_guardar)

	_menu_ver = PopupMenu.new()
	_menu_ver.name = "Ver"
	_menu_ver.add_check_item("Frames", 10)
	_menu_ver.set_item_checked(_menu_ver.get_item_index(10), true)
	_menu_ver.add_separator()
	_menu_ver.add_item("Zoom +", 0, KEY_EQUAL)
	_menu_ver.add_item("Zoom -", 1, KEY_MINUS)
	_menu_ver.add_item("Zoom 100%", 2, KEY_1)
	_menu_ver.add_item("Ajustar al canvas", 3, KEY_0)
	_menu_ver.id_pressed.connect(_on_ver_menu)
	mb.add_child(_menu_ver)

	return mb


func _build_dialogs() -> void:
	_dlg_client_folder = FileDialog.new()
	_dlg_client_folder.access = FileDialog.ACCESS_FILESYSTEM
	_dlg_client_folder.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	_dlg_client_folder.dir_selected.connect(_load_client_folder)
	add_child(_dlg_client_folder)

	_dlg_save_ind = FileDialog.new()
	_dlg_save_ind.access = FileDialog.ACCESS_FILESYSTEM
	_dlg_save_ind.file_mode = FileDialog.FILE_MODE_SAVE_FILE
	_dlg_save_ind.filters = ["*.ind;Graficos.ind"]
	_dlg_save_ind.current_file = "Graficos.ind"
	_dlg_save_ind.file_selected.connect(_save_ind_to_path)
	add_child(_dlg_save_ind)

	# Apply last known dir
	if not _recent_clients.is_empty():
		var last: String = _recent_clients[0]
		if DirAccess.dir_exists_absolute(last):
			_dlg_client_folder.current_dir = last

	# Index confirmation dialog
	_dlg_index_confirm = Window.new()
	_dlg_index_confirm.title = "Confirmar indexación"
	_dlg_index_confirm.size = Vector2i(520, 400)
	_dlg_index_confirm.exclusive = true
	_dlg_index_confirm.wrap_controls = true
	_dlg_index_confirm.close_requested.connect(func(): _dlg_index_confirm.hide())
	var vb := VBoxContainer.new()
	vb.set_anchors_preset(Control.PRESET_FULL_RECT)
	vb.add_theme_constant_override("separation", 8)
	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_child(vb)
	_dlg_index_confirm.add_child(margin)
	_index_preview_label = RichTextLabel.new()
	_index_preview_label.bbcode_enabled = true
	_index_preview_label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_index_preview_label.scroll_following = false
	vb.add_child(_index_preview_label)
	var btn_row := HBoxContainer.new()
	btn_row.alignment = BoxContainer.ALIGNMENT_END
	btn_row.add_theme_constant_override("separation", 8)
	vb.add_child(btn_row)
	var btn_cancel := Button.new()
	btn_cancel.text = "Cancelar"
	btn_cancel.pressed.connect(func(): _dlg_index_confirm.hide())
	btn_row.add_child(btn_cancel)
	var btn_confirm := Button.new()
	btn_confirm.text = "Confirmar e indexar"
	btn_confirm.pressed.connect(_on_index_confirmed)
	btn_row.add_child(btn_confirm)


func _connect_signals() -> void:
	# File list
	_file_list.file_selected.connect(_on_file_selected)

	# Toolbar
	_toolbar.tool_changed.connect(_on_tool_changed)
	_toolbar.detect_toggled.connect(func(on): _canvas.set_detect(on); _save_prefs())
	_toolbar.grid_toggled.connect(func(on): _canvas.set_grid_visible(on); _save_prefs())
	_toolbar.grid_config_changed.connect(func(cw, ch, lw, col):
		_canvas.set_grid_cell(cw, ch)
		_canvas.set_grid_line_w(lw)
		_canvas.set_grid_color(col)
		_save_prefs()
	)
	_toolbar.zoom_in_pressed.connect(func(): _canvas.zoom_in())
	_toolbar.zoom_out_pressed.connect(func(): _canvas.zoom_out())
	_toolbar.zoom_fit_pressed.connect(func(): _canvas.fit_to_canvas())
	_toolbar.zoom_reset_pressed.connect(func(): _canvas.zoom_reset())
	_toolbar.save_pressed.connect(_on_save_ind)
	_toolbar.index_pressed.connect(_on_index_image)

	# Canvas
	_canvas.frame_drawn.connect(_on_canvas_frame_drawn)
	_canvas.frame_selected.connect(_on_canvas_frame_selected)
	_canvas.multi_frame_selected.connect(_on_multi_frame_selected)
	_canvas.blob_clicked.connect(_on_canvas_blob_clicked)
	_canvas.frame_resized.connect(_on_canvas_frame_resized)
	_canvas.frame_delete_pressed.connect(func(idx): _delete_frame(idx))
	_canvas.frame_context_menu.connect(_on_canvas_context_menu)

	# Inspector
	_inspector.frame_selected.connect(_on_inspector_frame_selected)
	_inspector.frame_deleted.connect(_on_inspector_frame_deleted)
	_inspector.frame_props_changed.connect(_on_inspector_props_changed)
	_inspector.clear_frames_pressed.connect(_on_clear_frames)
	_inspector.detect_grid_pressed.connect(_on_detect_grid)
	_inspector.detect_blobs_pressed.connect(_on_detect_blobs)
	_inspector.detect_auto_pressed.connect(_on_detect_auto)
	_inspector.create_anim_pressed.connect(_on_create_anim_grh)
	_inspector.split_frame_pressed.connect(_on_split_frame)
	_inspector.save_init_pressed.connect(_on_save_init_file)
	_inspector.add_manual_frame_pressed.connect(_on_add_manual_frame)
	_inspector.index_frame_pressed.connect(_on_index_single_frame)
	_inspector.view_file_num_pressed.connect(_on_view_file_num)
	_inspector.next_grh_changed.connect(func(v): _next_grh_index = v)
	_inspector.save_body_pressed.connect(_on_save_body)
	_inspector.save_head_pressed.connect(_on_save_head)
	_inspector.save_helmet_pressed.connect(_on_save_helmet)
	_inspector.save_weapon_pressed.connect(_on_save_weapon)
	_inspector.save_shield_pressed.connect(_on_save_shield)
	_inspector.save_fx_pressed.connect(_on_save_fx)


# ── Keyboard shortcuts ───────────────────────────────────────────────────────

func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed:
		return
	var k := event as InputEventKey
	if k.ctrl_pressed:
		match k.keycode:
			KEY_S: _on_save_ind()
			KEY_O: _dlg_client_folder.popup_centered_ratio(0.7)
			KEY_Z:
				if k.shift_pressed:
					_redo()
				else:
					_undo()
			KEY_Y: _redo()
	else:
		match k.keycode:
			KEY_V, KEY_R, KEY_D: _toolbar.set_tool(0)  # Edit
			KEY_H: _toolbar.set_tool(1)  # Pan


# ── Tool mode ────────────────────────────────────────────────────────────────

func _on_tool_changed(mode: int) -> void:
	_canvas.tool_mode = mode
	_save_prefs()


## Save user preferences immediately (detect, grid, tool mode, recent clients).
func _save_prefs() -> void:
	_prefs.set_value("general", "recent_clients", _recent_clients)
	_prefs.set_value("session", "detect_enabled", _canvas.detect_enabled)
	_prefs.set_value("session", "show_grid", _canvas.show_grid)
	_prefs.set_value("session", "grid_cell_w", _canvas.grid_cell_w)
	_prefs.set_value("session", "grid_cell_h", _canvas.grid_cell_h)
	_prefs.set_value("session", "grid_line_w", _canvas.grid_line_width)
	_prefs.set_value("session", "grid_color", _canvas.grid_color.to_html(false))
	_prefs.set_value("session", "tool_mode", _canvas.tool_mode)
	_prefs.save(PREFS_PATH)


# ── Menu handlers ────────────────────────────────────────────────────────────

func _on_archivo_menu(id: int) -> void:
	match id:
		0: _dlg_client_folder.popup_centered_ratio(0.7)
		99:
			_save_session()
			get_tree().quit()


func _on_editar_menu(id: int) -> void:
	match id:
		0: _undo()
		1: _redo()
		2: _on_clear_frames()


func _on_guardar_menu(id: int) -> void:
	match id:
		0: _on_save_ind()
		1: _save_personajes_ind()
		2: _save_fxs_ind()
		3: _dlg_save_ind.popup_centered_ratio(0.7)


func _on_ver_menu(id: int) -> void:
	match id:
		0: _canvas.zoom_in()
		1: _canvas.zoom_out()
		2: _canvas.zoom_reset()
		3: _canvas.fit_to_canvas()
		10:
			# Toggle Frames overlay visibility
			var idx := _menu_ver.get_item_index(10)
			var checked := not _menu_ver.is_item_checked(idx)
			_menu_ver.set_item_checked(idx, checked)
			_canvas.show_frames = checked
			_canvas.queue_redraw()


# ── Load client folder ──────────────────────────────────────────────────────

func _load_client_folder(path: String) -> void:
	_using_client = true
	_client_graficos_path = path
	_push_recent_client(path)

	var try_graficos := [path.path_join("Data/Graficos"), path.path_join("Graficos")]
	var try_init := [path.path_join("Data/INIT"), path.path_join("INIT")]

	_graficos_folder_path = ""
	for p in try_graficos:
		if DirAccess.dir_exists_absolute(p):
			_graficos_folder_path = p
			break

	_init_folder = ""
	for p in try_init:
		if DirAccess.dir_exists_absolute(p):
			_init_folder = p
			break

	if _graficos_folder_path.is_empty() and _init_folder.is_empty():
		_update_status("No se encontraron subcarpetas Graficos/ ni INIT/ en: " + path)
		return

	var msgs: PackedStringArray = PackedStringArray()

	# Load INIT data
	if not _init_folder.is_empty():
		var grh_path := _init_folder.path_join("Graficos.ind")
		if FileAccess.file_exists(grh_path):
			_grh_data = GrhIO.load_ind(grh_path)
			_ind_path = grh_path
			_next_grh_index = _grh_data["max_index"] + 1
			_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
			msgs.append("Graficos.ind: %d GRHs" % _grh_data["entries"].size())

		var body_path := _init_folder.path_join("Personajes.ind")
		if FileAccess.file_exists(body_path):
			_bodies_data = _load_personajes_ind(body_path)
			msgs.append("Personajes.ind: %d cuerpos" % _bodies_data.size())

		var fxs_path := _init_folder.path_join("Fxs.ind")
		if FileAccess.file_exists(fxs_path):
			_load_fxs_ind(fxs_path)
			msgs.append("Fxs.ind: %d efectos" % _fxs_data.size())

		# Load indices.ini for texture indexing
		var indices_path := _init_folder.path_join("indices.ini")
		if FileAccess.file_exists(indices_path):
			var f := FileAccess.open(indices_path, FileAccess.READ)
			if f != null:
				_indices_ini_data = f.get_as_text().split("\n")
				f.close()
				_indices_categories = _parse_indices_categories(_indices_ini_data)
				msgs.append("indices.ini: %d categorías" % _indices_categories.size())

		_inspector.load_init_files(_init_folder)
		msgs.append("INIT: archivos cargados")

	# Load images
	if not _graficos_folder_path.is_empty():
		_file_list._using_client = true
		_file_list.load_folder(_graficos_folder_path)
		msgs.append("Graficos: %d imagenes" % _file_list.get_file_count())

	_update_status("Cliente cargado: " + " | ".join(msgs))



# ── File selection ───────────────────────────────────────────────────────────

func _on_file_selected(path: String, file_num: int) -> void:
	_current_image_path = path
	_current_file_num = file_num
	_current_frames = []
	_selected_frame_idx = -1

	# Cancel any pending analysis
	_finish_analysis_thread()

	var img := _load_image_from_os_path(path)
	if img == null:
		_update_status("No se pudo cargar: " + path)
		return

	_current_image = img
	_current_texture = ImageTexture.create_from_image(img)
	_canvas.load_image(img)
	_inspector.set_image(img)

	# Load existing GRHs for this file (fast — just dict lookup)
	if _grh_data["entries"].size() > 0 and file_num > 0:
		var entries := _get_grh_entries_for_file_num(file_num)
		for e in entries:
			_current_frames.append({
				"sx": e.get("sx", 0), "sy": e.get("sy", 0),
				"w": e.get("width", 32), "h": e.get("height", 32),
				"grh_index": e.grh_index, "file_num": file_num
			})

	_refresh_all()

	# Update GRH viewer (fast)
	var grh_entries := _get_grh_entries_for_file_num(file_num)
	_inspector.update_grh_viewer(grh_entries, _current_texture)

	var px_count := img.get_width() * img.get_height()
	if px_count > 512 * 512:
		# Large image → run blob analysis in background thread
		_update_status("%s — FileNum=%d — %dx%d px — analizando..." % [
			path.get_file(), file_num, img.get_width(), img.get_height()])
		_show_loading("Analizando %s (%dx%d)..." % [path.get_file(), img.get_width(), img.get_height()])
		_analysis_thread = Thread.new()
		_analysis_pending = true
		# Duplicate image for thread safety (Image is not thread-safe)
		var img_copy := img.duplicate()
		_analysis_thread.start(_run_analysis.bind(img_copy, file_num))
	else:
		# Small image → run synchronously
		_run_analysis_sync(img, file_num)


func _show_loading(text: String) -> void:
	if _loading_overlay != null:
		_loading_label.text = text
		_loading_overlay.visible = true


func _hide_loading() -> void:
	if _loading_overlay != null:
		_loading_overlay.visible = false


func _run_analysis(img: Image, file_num: int) -> Dictionary:
	# Runs in background thread — NO Godot scene tree calls here
	var blob_data := FrameDetector.detect_blobs_indexed(img, 0.03, 3, 1)
	var content_regions := FrameDetector.detect_content_rows(img, 0.03, 3, 1)
	var rects: Array = blob_data["rects"]
	return {
		"blob_data": blob_data,
		"content_regions": content_regions,
		"rects": rects,
		"file_num": file_num
	}


func _finish_analysis_thread() -> void:
	if _analysis_thread != null and _analysis_thread.is_started():
		_analysis_thread.wait_to_finish()
		_analysis_thread = null
	_analysis_pending = false
	_hide_loading()


func _run_analysis_sync(img: Image, file_num: int) -> void:
	_update_status("%s — FileNum=%d — %dx%d px (analizando blobs...)" % [
		_current_image_path.get_file(), file_num, img.get_width(), img.get_height()])

	var blob_data := FrameDetector.detect_blobs_indexed(img, 0.03, 3, 1)
	var content_regions := FrameDetector.detect_content_rows(img, 0.03, 3, 1)
	var rects: Array = blob_data["rects"]
	_apply_analysis_results(blob_data, content_regions, rects, file_num)


func _apply_analysis_results(blob_data: Dictionary, content_regions: Array, rects: Array, file_num: int) -> void:
	_canvas.set_blob_data(blob_data["map"], blob_data["rects"], blob_data["id_to_rect"], blob_data["width"])
	_canvas.set_content_regions(content_regions)

	var snap_hint := _detect_snap_hint(rects, content_regions)

	# Detect related animations
	var related := _find_related_animations(file_num)
	_inspector.update_related_animations(related)
	var all_related_frames: Array = []
	for anim in related:
		for fr in anim.get("frames", []):
			all_related_frames.append(fr)
	_inspector.set_related_frames(all_related_frames)
	_load_related_textures(related, file_num, _current_image)

	var related_info := ""
	if not related.is_empty():
		related_info = " — %d animaciones" % related.size()

	_update_status("%s — FileNum=%d — %d GRHs — %d blobs — %s%s" % [
		_current_image_path.get_file(), file_num, _current_frames.size(), rects.size(), snap_hint, related_info])


# ── Canvas signals ───────────────────────────────────────────────────────────

func _on_canvas_frame_drawn(rect: Rect2) -> void:
	_push_undo()
	var file_num := _current_file_num
	var grh_idx := _next_grh_index
	_next_grh_index += 1
	_inspector.set_next_grh(_next_grh_index)

	var sx := _clamp_x(int(rect.position.x))
	var sy := _clamp_y(int(rect.position.y))
	var frame := {
		"sx": sx, "sy": sy,
		"w": _clamp_w(sx, int(rect.size.x)),
		"h": _clamp_h(sy, int(rect.size.y)),
		"grh_index": grh_idx, "file_num": file_num
	}
	_current_frames.append(frame)
	_selected_frame_idx = _current_frames.size() - 1
	_refresh_all()
	_update_status("Frame dibujado: G%d (%d,%d) %dx%d" % [
		grh_idx, frame.sx, frame.sy, frame.w, frame.h])


func _on_canvas_blob_clicked(rect: Rect2i) -> void:
	_on_canvas_frame_drawn(Rect2(rect.position, rect.size))


func _on_canvas_frame_resized(index: int, new_rect: Rect2) -> void:
	if index < 0 or index >= _current_frames.size():
		return
	_push_undo()
	var f: Dictionary = _current_frames[index]
	f["sx"] = _clamp_x(int(new_rect.position.x))
	f["sy"] = _clamp_y(int(new_rect.position.y))
	f["w"] = _clamp_w(f["sx"], int(new_rect.size.x))
	f["h"] = _clamp_h(f["sy"], int(new_rect.size.y))
	_current_frames[index] = f
	_refresh_all()


func _on_canvas_frame_selected(frame_idx: int) -> void:
	_selected_frame_idx = frame_idx
	_refresh_all()


func _on_multi_frame_selected(indices: Array) -> void:
	if indices.size() >= 2:
		var frame_dicts: Array = []
		for idx in indices:
			if idx >= 0 and idx < _current_frames.size():
				frame_dicts.append(_current_frames[idx])
		if frame_dicts.size() >= 2:
			var textures: Dictionary = {}
			textures[_current_file_num] = _current_texture
			_inspector.set_anim_frames(frame_dicts, _current_texture, textures)
	else:
		_inspector.clear_anim()


# ── Inspector signals ────────────────────────────────────────────────────────

func _on_inspector_frame_selected(idx: int) -> void:
	_selected_frame_idx = idx
	_canvas.set_selected(idx)
	_refresh_all()


func _on_inspector_frame_deleted(idx: int) -> void:
	if idx == -1:
		idx = _selected_frame_idx
	_delete_frame(idx)


func _on_inspector_props_changed(idx: int, sx: int, sy: int, w: int, h: int, grh: int) -> void:
	if idx == -1:
		idx = _selected_frame_idx
	if idx < 0 or idx >= _current_frames.size():
		return
	_push_undo()
	var f: Dictionary = _current_frames[idx]
	f["sx"] = _clamp_x(sx)
	f["sy"] = _clamp_y(sy)
	f["w"] = _clamp_w(f["sx"], w)
	f["h"] = _clamp_h(f["sy"], h)
	f["grh_index"] = grh
	_current_frames[idx] = f
	_refresh_all()


func _on_clear_frames() -> void:
	_push_undo()
	_current_frames = []
	_selected_frame_idx = -1
	_refresh_all()
	_update_status("Frames limpiados.")


func _on_detect_grid(cell_w: int, cell_h: int, off_x: int, off_y: int, mrg_x: int, mrg_y: int, skip_empty: bool) -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return
	var detected := FrameDetector.detect_grid(_current_image, cell_w, cell_h, off_x, off_y, mrg_x, mrg_y, skip_empty)
	_apply_detected_frames(detected)
	_update_status("Grid: %d frames detectados (%dx%d px)" % [detected.size(), cell_w, cell_h])


func _on_detect_blobs(alpha: float, min_size: int, padding: int) -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return
	var blob_data := FrameDetector.detect_blobs_indexed(_current_image, alpha, min_size, padding)
	var rects: Array = blob_data["rects"]
	_canvas.set_blob_data(blob_data["map"], blob_data["rects"], blob_data["id_to_rect"], blob_data["width"])

	# Update content regions for smart mode
	var content_regions := FrameDetector.detect_content_rows(_current_image, alpha, 3, padding)
	_canvas.set_content_regions(content_regions)

	if rects.is_empty():
		_update_status("No se detectaron blobs.")
		return

	# Analyze blob sizes — if uniform, create a grid covering the full image
	var grid_result := _try_uniform_blob_grid(rects)
	if grid_result.size() > 0:
		_apply_detected_frames(grid_result)
		var cell: Dictionary = grid_result[0]
		_update_status("Blobs→Grid: %d frames (%dx%d px, filas adaptativas)" % [grid_result.size(), cell.w, cell.h])
	else:
		_apply_detected_frames(rects)
		_update_status("Blobs: %d regiones detectadas" % rects.size())


func _on_detect_auto() -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return
	var result := FrameDetector.detect_auto_frames(_current_image)
	var frames: Array = result.get("frames", [])
	var strategy: String = result.get("strategy", "?")
	var blob_count: int = result.get("blobs", 0)
	var grid_cell: Vector2i = result.get("grid_cell", Vector2i.ZERO)

	# Update canvas content regions for Smart hover mode
	_canvas.set_content_regions(frames)

	if frames.is_empty():
		_update_status("Auto-detect: no se detectaron frames.")
		return

	# Convert Rect2i to frame dicts
	var frame_dicts: Array = []
	for rect in frames:
		frame_dicts.append({"sx": rect.position.x, "sy": rect.position.y, "w": rect.size.x, "h": rect.size.y})
	_apply_detected_frames(frame_dicts)

	if strategy == "grid":
		_update_status("Auto: %d frames (grid %dx%d, %d blobs)" % [frames.size(), grid_cell.x, grid_cell.y, blob_count])
	else:
		_update_status("Auto: %d frames (mixed sizes, %d blobs)" % [frames.size(), blob_count])


func _on_split_frame(cell_w: int, cell_h: int) -> void:
	if _selected_frame_idx < 0 or _selected_frame_idx >= _current_frames.size():
		_update_status("Selecciona un frame primero.")
		return
	var f: Dictionary = _current_frames[_selected_frame_idx]
	var cols: int = f.w / cell_w
	var rows: int = f.h / cell_h
	if cols <= 0 or rows <= 0:
		_update_status("Frame (%dx%d) mas pequeno que celda (%dx%d)." % [f.w, f.h, cell_w, cell_h])
		return
	if cols == 1 and rows == 1:
		_update_status("Solo cabe 1 celda — nada que dividir.")
		return
	_push_undo()
	var file_num: int = f.file_num
	var base_x: int = f.sx
	var base_y: int = f.sy
	var insert_at: int = _selected_frame_idx
	_current_frames.remove_at(_selected_frame_idx)
	for row in range(rows):
		for col in range(cols):
			var sub := {
				"sx": base_x + col * cell_w, "sy": base_y + row * cell_h,
				"w": cell_w, "h": cell_h,
				"grh_index": _next_grh_index, "file_num": file_num
			}
			_current_frames.insert(insert_at + row * cols + col, sub)
			_next_grh_index += 1
	_inspector.set_next_grh(_next_grh_index)
	_selected_frame_idx = insert_at
	_refresh_all()
	_update_status("Dividido en %dx%d = %d celdas de %dx%d px" % [cols, rows, cols * rows, cell_w, cell_h])


func _on_create_anim_grh(indices: Array[int], speed: float) -> void:
	var missing: Array[int] = []
	for fi in indices:
		var e = _grh_data["entries"].get(fi, null)
		if e == null or e.get("num_frames", 1) != 1:
			missing.append(fi)
	if not missing.is_empty():
		_update_status("GRH no encontrados como estaticos: %s" % str(missing))
		return
	var new_idx := _next_grh_index
	_next_grh_index += 1
	_inspector.set_next_grh(_next_grh_index)
	var entry := {
		"grh_index": new_idx,
		"num_frames": indices.size(),
		"frames": indices,
		"speed": speed
	}
	_grh_data["entries"][new_idx] = entry
	if new_idx > _grh_data["max_index"]:
		_grh_data["max_index"] = new_idx
	_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
	_update_status("GRH Animado G%d creado: %d frames @ %.2f speed" % [new_idx, indices.size(), speed])


func _on_save_init_file(path: String, content: String) -> void:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f != null:
		f.store_string(content)
		f.close()
		_update_status("Guardado: " + path.get_file())
	else:
		_update_status("Error al guardar: " + path)


func _on_add_manual_frame() -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return
	_push_undo()
	var frame := {
		"sx": 0, "sy": 0, "w": 32, "h": 32,
		"grh_index": _next_grh_index,
		"file_num": _current_file_num
	}
	_current_frames.append(frame)
	_next_grh_index += 1
	_inspector.set_next_grh(_next_grh_index)
	_selected_frame_idx = _current_frames.size() - 1
	_refresh_all()


# ── Frame management ─────────────────────────────────────────────────────────

func _delete_frame(idx: int) -> void:
	if idx < 0 or idx >= _current_frames.size():
		return
	_push_undo()
	_current_frames.remove_at(idx)
	_selected_frame_idx = mini(_selected_frame_idx, _current_frames.size() - 1)
	_refresh_all()
	_update_status("Frame eliminado. Quedan %d." % _current_frames.size())


func _apply_detected_frames(detected: Array) -> void:
	if detected.is_empty():
		_update_status("No se detectaron frames. Ajusta los parametros.")
		return
	_push_undo()
	_current_frames = []
	for det in detected:
		var sx: int; var sy: int; var sw: int; var sh: int
		if det is Rect2i:
			sx = det.position.x; sy = det.position.y; sw = det.size.x; sh = det.size.y
		elif det is Dictionary:
			sx = det.get("sx", 0); sy = det.get("sy", 0)
			sw = det.get("w", 32); sh = det.get("h", 32)
		else:
			continue
		_current_frames.append({
			"sx": sx, "sy": sy, "w": sw, "h": sh,
			"grh_index": _next_grh_index, "file_num": _current_file_num
		})
		_next_grh_index += 1
	_inspector.set_next_grh(_next_grh_index)
	_selected_frame_idx = -1
	_refresh_all()


# ── Undo / Redo ──────────────────────────────────────────────────────────────

func _push_undo() -> void:
	var snapshot := {
		"frames": _current_frames.duplicate(true),
		"selected": _selected_frame_idx,
		"next_grh": _next_grh_index
	}
	_undo_stack.append(snapshot)
	if _undo_stack.size() > MAX_UNDO:
		_undo_stack.pop_front()
	_redo_stack.clear()


func _undo() -> void:
	if _undo_stack.is_empty():
		_update_status("Nada que deshacer.")
		return
	# Save current state for redo
	_redo_stack.append({
		"frames": _current_frames.duplicate(true),
		"selected": _selected_frame_idx,
		"next_grh": _next_grh_index
	})
	var snapshot: Dictionary = _undo_stack.pop_back()
	_current_frames = snapshot["frames"]
	_selected_frame_idx = snapshot["selected"]
	_next_grh_index = snapshot["next_grh"]
	_inspector.set_next_grh(_next_grh_index)
	_refresh_all()
	_update_status("Deshacer. (%d en pila)" % _undo_stack.size())


func _redo() -> void:
	if _redo_stack.is_empty():
		_update_status("Nada que rehacer.")
		return
	_undo_stack.append({
		"frames": _current_frames.duplicate(true),
		"selected": _selected_frame_idx,
		"next_grh": _next_grh_index
	})
	var snapshot: Dictionary = _redo_stack.pop_back()
	_current_frames = snapshot["frames"]
	_selected_frame_idx = snapshot["selected"]
	_next_grh_index = snapshot["next_grh"]
	_inspector.set_next_grh(_next_grh_index)
	_refresh_all()
	_update_status("Rehacer. (%d en pila)" % _redo_stack.size())


# ── Image bounds clamping ────────────────────────────────────────────────────

func _clamp_x(x: int) -> int:
	# Allow negative offsets for centering (sprite can extend beyond image)
	if _current_image == null: return x
	return clampi(x, -4096, _current_image.get_width() - 1)

func _clamp_y(y: int) -> int:
	if _current_image == null: return y
	return clampi(y, -4096, _current_image.get_height() - 1)

func _clamp_w(x: int, w: int) -> int:
	return maxi(w, 1)

func _clamp_h(y: int, h: int) -> int:
	return maxi(h, 1)


## Analyze detected blobs and auto-set the best snap mode.
## Returns a human-readable hint string for the status bar.
func _detect_snap_hint(rects: Array, content_regions: Array = []) -> String:
	if rects.size() < 2:
		return "Snap: Pot.2"

	# Collect all blob sizes (w, h)
	var sizes: Array = []  # Array of Vector2i
	for r in rects:
		if r is Rect2i and r.size.x >= 4 and r.size.y >= 4:
			sizes.append(Vector2i(r.size.x, r.size.y))

	if sizes.is_empty():
		return "Snap: Pot.2"

	# Group similar sizes (tolerance ±4px in each dimension)
	const TOL := 4
	var groups: Array = []  # [{w, h, count}]
	for s in sizes:
		var found := false
		for g in groups:
			if absi(s.x - g["w"]) <= TOL and absi(s.y - g["h"]) <= TOL:
				g["count"] += 1
				found = true
				break
		if not found:
			groups.append({"w": s.x, "h": s.y, "count": 1})

	# Sort by count descending
	groups.sort_custom(func(a, b): return a["count"] > b["count"])

	var best: Dictionary = groups[0]
	var ratio: float = float(best["count"]) / float(sizes.size())

	# Check if best size is already a power of 2
	var bw: int = best["w"]
	var bh: int = best["h"]
	var w_is_pow2: bool = (bw & (bw - 1)) == 0 and bw > 0
	var h_is_pow2: bool = (bh & (bh - 1)) == 0 and bh > 0

	# Always use Smart mode — auto-detect just reports what it found
	# Preserve user's detect preference (don't force on)

	if ratio >= 0.5 and best["count"] >= 3:
		var gw: int = best["w"]
		var gh: int = best["h"]
		return "Smart (auto — %d/%d blobs %dx%d)" % [best["count"], sizes.size(), gw, gh]
	elif content_regions.size() >= 2:
		return "Smart (auto — %d regiones de contenido)" % content_regions.size()
	else:
		return "Smart (auto — %d blobs detectados)" % rects.size()


## When blobs are uniform size, generate a full grid covering the entire image.
## Each row is scanned independently: cells that have content get a frame,
## empty cells are skipped. This handles sprite sheets where some rows have
## more frames than others (e.g. 6-frame walk N/S vs 5-frame walk E/W).
## Returns empty array if blobs aren't uniform enough for grid conversion.
func _try_uniform_blob_grid(rects: Array) -> Array:
	if rects.size() < 3 or _current_image == null:
		return []

	# Collect blob sizes
	var sizes: Array = []
	for r in rects:
		var rr: Rect2i = r if r is Rect2i else Rect2i(r.get("sx", 0), r.get("sy", 0), r.get("w", 32), r.get("h", 32))
		if rr.size.x >= 4 and rr.size.y >= 4:
			sizes.append(Vector2i(rr.size.x, rr.size.y))

	if sizes.size() < 3:
		return []

	# Group by ±4px tolerance
	const TOL := 4
	var groups: Array = []
	for s in sizes:
		var found := false
		for g in groups:
			if absi(s.x - g["w"]) <= TOL and absi(s.y - g["h"]) <= TOL:
				g["count"] += 1
				# Track average
				g["sum_w"] += s.x
				g["sum_h"] += s.y
				found = true
				break
		if not found:
			groups.append({"w": s.x, "h": s.y, "count": 1, "sum_w": s.x, "sum_h": s.y})

	groups.sort_custom(func(a, b): return a["count"] > b["count"])
	var best: Dictionary = groups[0]
	var ratio: float = float(best["count"]) / float(sizes.size())

	# Need at least 50% of blobs to be the same size
	if ratio < 0.5 or best["count"] < 3:
		return []

	# Use the average of the dominant group as cell size
	var cell_w: int = best["sum_w"] / best["count"]
	var cell_h: int = best["sum_h"] / best["count"]
	if cell_w < 4 or cell_h < 4:
		return []

	# Generate grid: iterate rows of cell_h, then columns of cell_w
	# Per cell, check if it has non-empty content (skip empty cells)
	# Include partial edge cells if they are >= 70% of cell size
	var img_w: int = _current_image.get_width()
	var img_h: int = _current_image.get_height()
	var min_cw: int = int(cell_w * 0.70)
	var min_ch: int = int(cell_h * 0.70)
	var result: Array = []

	var row_y := 0
	while row_y < img_h:
		var actual_h: int = mini(cell_h, img_h - row_y)
		if actual_h < min_ch:
			break
		var col_x := 0
		while col_x < img_w:
			var actual_w: int = mini(cell_w, img_w - col_x)
			if actual_w < min_cw:
				break
			if not FrameDetector._is_empty_sample(_current_image, col_x, row_y, actual_w, actual_h):
				result.append({"sx": col_x, "sy": row_y, "w": actual_w, "h": actual_h})
			col_x += cell_w
		row_y += cell_h

	# If grid produces fewer frames than blobs found, it's probably not a good fit
	if result.size() < rects.size() * 0.5:
		return []

	return result


func _refresh_all() -> void:
	_canvas.set_frames(_current_frames)
	_canvas.set_selected(_selected_frame_idx)
	_inspector.set_grh_entries(_grh_data["entries"])
	_inspector.set_current_texture(_current_texture)
	_inspector.set_current_file_num(_current_file_num)
	_inspector.update_frames(_current_frames, _selected_frame_idx)
	if _selected_frame_idx >= 0 and _selected_frame_idx < _current_frames.size():
		_inspector.update_selected_props(_current_frames[_selected_frame_idx])
	else:
		_inspector.clear_props()


# ── Indexing ─────────────────────────────────────────────────────────────────

func _on_index_image() -> void:
	if _current_frames.is_empty():
		_update_status("No hay frames definidos para esta imagen.")
		return

	# Build preview of what will change
	_index_pending_entries.clear()
	var lines := "[b]Imagen:[/b] %s  (FileNum %d)\n" % [_current_image_path.get_file(), _current_file_num]
	lines += "[b]Frames a indexar:[/b] %d\n\n" % _current_frames.size()

	var new_count := 0
	var overwrite_count := 0
	for frame in _current_frames:
		var grh: int = frame.grh_index
		var entry := {
			"grh_index": grh,
			"num_frames": 1,
			"file_num": frame.file_num,
			"sx": frame.sx, "sy": frame.sy,
			"width": frame.w, "height": frame.h
		}
		_index_pending_entries.append(entry)
		var exists: bool = _grh_data["entries"].has(grh)
		if exists:
			overwrite_count += 1
			lines += "[color=yellow]SOBREESCRIBIR[/color] "
		else:
			new_count += 1
			lines += "[color=lime]NUEVO[/color]         "
		lines += "GRH %d  →  File %d  (%d,%d  %dx%d)\n" % [
			grh, frame.file_num, frame.sx, frame.sy, frame.w, frame.h]

	lines += "\n[b]Resumen:[/b] %d nuevos, %d sobreescritos" % [new_count, overwrite_count]
	if not _ind_path.is_empty():
		lines += "\n[b]Archivo:[/b] %s" % _ind_path.get_file()

	_index_preview_label.text = lines
	if not _dlg_index_confirm.is_inside_tree():
		add_child(_dlg_index_confirm)
	_dlg_index_confirm.popup_centered()


func _on_index_confirmed() -> void:
	_dlg_index_confirm.hide()
	var added := 0
	for entry in _index_pending_entries:
		_grh_data["entries"][entry.grh_index] = entry
		if entry.grh_index > _grh_data["max_index"]:
			_grh_data["max_index"] = entry.grh_index
		added += 1
	_index_pending_entries.clear()
	_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
	_refresh_all()
	# Auto-save .ind if path is known
	if not _ind_path.is_empty():
		_save_ind_to_path(_ind_path)
		_update_status("Indexados %d frames y guardado en %s" % [added, _ind_path.get_file()])
	else:
		_update_status("Indexados %d frames (no guardado — usa Guardar)" % added)


func _on_index_single_frame(idx: int) -> void:
	if idx < 0 or idx >= _current_frames.size():
		return
	var frame: Dictionary = _current_frames[idx]
	var entry := {
		"grh_index": frame.grh_index,
		"num_frames": 1,
		"file_num": frame.file_num,
		"sx": frame.sx, "sy": frame.sy,
		"width": frame.w, "height": frame.h
	}
	_grh_data["entries"][frame.grh_index] = entry
	if frame.grh_index > _grh_data["max_index"]:
		_grh_data["max_index"] = frame.grh_index
	_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
	_refresh_all()
	_update_status("GRH %d indexado. Total: %d entradas." % [frame.grh_index, _grh_data["entries"].size()])


## Navigate to another graphic file and optionally select a frame by GRH index.
var _pending_select_grh: int = 0

func _on_view_file_num(file_num: int, grh_index: int) -> void:
	# Defer to next frame — the button that triggered this is about to be freed
	_pending_select_grh = grh_index
	call_deferred("_do_navigate_to_file", file_num)


func _do_navigate_to_file(file_num: int) -> void:
	_file_list.select_by_file_num(file_num)
	# After file loads, select the frame matching grh_index
	if _pending_select_grh > 0:
		for i in range(_current_frames.size()):
			if _current_frames[i].get("grh_index", 0) == _pending_select_grh:
				_selected_frame_idx = i
				_refresh_all()
				break
		_pending_select_grh = 0


# ── Save ─────────────────────────────────────────────────────────────────────

func _on_save_ind() -> void:
	if _ind_path.is_empty() and _init_folder.is_empty():
		_update_status("Abrí una carpeta de cliente primero.")
		return
	_show_save_confirm_dialog()


func _show_save_confirm_dialog() -> void:
	# Build diff summary of all pending changes
	var lines: PackedStringArray = []
	lines.append("[b]Cambios pendientes:[/b]\n")

	if not _ind_path.is_empty():
		lines.append("[color=#8f8]• Graficos.ind[/color] → %d entradas (max GRH: %d)" % [_grh_data["entries"].size(), _grh_data["max_index"]])

	var ini_path := _get_init_path("indices.ini")
	if not ini_path.is_empty() and _indices_ini_data.size() > 0:
		# Count references
		var ref_count := 0
		for line in _indices_ini_data:
			if line.strip_edges().begins_with("Referencias="):
				ref_count = int(line.strip_edges().substr(12))
				break
		lines.append("[color=#8f8]• indices.ini[/color] → %d referencias" % ref_count)

	# Check other modified .ind files
	if _bodies_data.size() > 0:
		lines.append("[color=#8f8]• Personajes.ind[/color] → %d entradas" % _bodies_data.size())
	if _fxs_data.size() > 0:
		lines.append("[color=#8f8]• FXs.ind[/color] → %d entradas" % _fxs_data.size())

	if lines.size() <= 1:
		_update_status("No hay cambios pendientes.")
		return

	lines.append("")
	lines.append("[color=#aaa]Se escribirán estos archivos al cliente.[/color]")
	lines.append("[color=#aaa]Esta acción sobrescribe los archivos originales.[/color]")

	_ensure_confirm_dialog()
	_index_preview_label_save.text = "\n".join(lines)
	if not _dlg_save_confirm.is_inside_tree():
		add_child(_dlg_save_confirm)
	_dlg_save_confirm.popup_centered()


func _on_save_confirmed() -> void:
	_dlg_save_confirm.hide()
	var saved: PackedStringArray = []

	# Save Graficos.ind
	if not _ind_path.is_empty():
		if GrhIO.save_ind(_ind_path, _grh_data):
			saved.append("Graficos.ind")

	# Save indices.ini
	var ini_path := _get_init_path("indices.ini")
	if not ini_path.is_empty() and _indices_ini_data.size() > 0:
		var f := FileAccess.open(ini_path, FileAccess.WRITE)
		if f != null:
			f.store_string("\n".join(_indices_ini_data))
			f.close()
			saved.append("indices.ini")

	_dirty = false
	if saved.size() > 0:
		_update_status("Guardado: %s" % ", ".join(saved))
	else:
		_update_status("No se guardó ningún archivo.")


func _save_ind_to_path(path: String) -> void:
	_ind_path = path
	if GrhIO.save_ind(path, _grh_data):
		_update_status("Guardado: %s (%d entradas, max=%d)" % [path, _grh_data["entries"].size(), _grh_data["max_index"]])
	else:
		_update_status("Error al guardar: " + path)


func _save_personajes_ind() -> void:
	if _bodies_data.is_empty():
		_update_status("No hay datos de Personajes.ind.")
		return
	var path := _init_folder.path_join("Personajes.ind")
	if not FileAccess.file_exists(path):
		_update_status("No se encontro Personajes.ind en: " + _init_folder)
		return
	var orig := FileAccess.open(path, FileAccess.READ)
	if orig == null:
		_update_status("No se pudo leer Personajes.ind")
		return
	var header: PackedByteArray = orig.get_buffer(263)
	orig.close()
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("No se pudo escribir Personajes.ind")
		return
	f.store_buffer(header)
	f.store_16(_bodies_data.size())
	for b in _bodies_data:
		f.store_16(b.walk_n & 0xFFFF)
		f.store_16(b.walk_e & 0xFFFF)
		f.store_16(b.walk_s & 0xFFFF)
		f.store_16(b.walk_w & 0xFFFF)
		f.store_16(b.head_x & 0xFFFF)
		f.store_16(b.head_y & 0xFFFF)
	f.close()
	_update_status("Personajes.ind guardado (%d cuerpos)" % _bodies_data.size())


func _save_fxs_ind() -> void:
	if _fxs_data.is_empty():
		_update_status("No hay datos de Fxs.ind.")
		return
	var path := _init_folder.path_join("Fxs.ind")
	if not FileAccess.file_exists(path):
		_update_status("No se encontro Fxs.ind en: " + _init_folder)
		return
	if _mi_cabecera_fxs.is_empty():
		var orig := FileAccess.open(path, FileAccess.READ)
		if orig != null:
			_mi_cabecera_fxs = orig.get_buffer(263)
			orig.close()
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("No se pudo escribir Fxs.ind")
		return
	f.store_buffer(_mi_cabecera_fxs)
	f.store_16(_fxs_data.size())
	for e in _fxs_data:
		f.store_16(e.animacion & 0xFFFF)
		f.store_16(e.offset_x & 0xFFFF)
		f.store_16(e.offset_y & 0xFFFF)
	f.close()
	_update_status("Fxs.ind guardado (%d efectos)" % _fxs_data.size())


# ── Data helpers ─────────────────────────────────────────────────────────────

func _get_grh_entries_for_file_num(file_num: int) -> Array:
	var result: Array = []
	for key in _grh_data["entries"]:
		var e: Dictionary = _grh_data["entries"][key]
		if e.get("num_frames", 1) == 1 and e.get("file_num", -1) == file_num:
			result.append(e)
	result.sort_custom(func(a, b): return a.grh_index < b.grh_index)
	return result


## Resolve an animated GRH into an array of frame dicts {sx, sy, w, h, grh_index, file_num}.
## Returns empty array if grh_index is not found or not animated.
func _resolve_anim_grh(grh_index: int) -> Array:
	var entry = _grh_data["entries"].get(grh_index, null)
	if entry == null:
		return []
	var nf: int = entry.get("num_frames", 1)
	if nf <= 1:
		# Static GRH — return as single-frame "animation"
		return [{"sx": entry.get("sx", 0), "sy": entry.get("sy", 0),
				"w": entry.get("width", 32), "h": entry.get("height", 32),
				"grh_index": grh_index, "file_num": entry.get("file_num", 0)}]
	# Animated: resolve each frame index
	var frames: Array = []
	var frame_indices: Array = entry.get("frame_indices", [])
	for fi in frame_indices:
		var fe = _grh_data["entries"].get(fi, null)
		if fe != null and fe.get("num_frames", 1) == 1:
			frames.append({"sx": fe.get("sx", 0), "sy": fe.get("sy", 0),
				"w": fe.get("width", 32), "h": fe.get("height", 32),
				"grh_index": fi, "file_num": fe.get("file_num", 0)})
	return frames


## Check if an animated GRH references any static GRH from the given file_num.
func _anim_uses_file(grh_index: int, file_num: int) -> bool:
	var entry = _grh_data["entries"].get(grh_index, null)
	if entry == null:
		return false
	var nf: int = entry.get("num_frames", 1)
	if nf <= 1:
		return entry.get("file_num", -1) == file_num
	for fi in entry.get("frame_indices", []):
		var fe = _grh_data["entries"].get(fi, null)
		if fe != null and fe.get("file_num", -1) == file_num:
			return true
	return false


## Find all body and FX animations that reference GRHs from the given file_num.
## Returns Array of {label, grh_index, frames, speed, source} for the inspector.
func _find_related_animations(file_num: int) -> Array:
	var result: Array = []
	var entries: Dictionary = _grh_data["entries"]
	if entries.is_empty():
		return result

	var heading_names := ["Norte", "Este", "Sur", "Oeste"]
	var heading_keys := ["walk_n", "walk_e", "walk_s", "walk_w"]

	# Search bodies
	for body in _bodies_data:
		var body_idx: int = body.get("index", 0)
		var found_any := false
		for h in range(4):
			var walk_grh: int = body.get(heading_keys[h], 0)
			if walk_grh > 0 and _anim_uses_file(walk_grh, file_num):
				found_any = true
		if found_any:
			for h in range(4):
				var walk_grh: int = body.get(heading_keys[h], 0)
				if walk_grh <= 0:
					continue
				var frames := _resolve_anim_grh(walk_grh)
				if frames.is_empty():
					continue
				var entry = entries.get(walk_grh, {})
				var speed: float = entry.get("speed", 100.0) if entry.get("num_frames", 1) > 1 else 100.0
				result.append({
					"label": "Body %d — %s" % [body_idx, heading_names[h]],
					"grh_index": walk_grh,
					"frames": frames,
					"speed": speed,
					"source": "Personajes.ind"
				})

	# Search FXs
	for fx in _fxs_data:
		var fx_idx: int = fx.get("index", 0)
		var anim_grh: int = fx.get("animacion", 0)
		if anim_grh > 0 and _anim_uses_file(anim_grh, file_num):
			var frames := _resolve_anim_grh(anim_grh)
			if frames.is_empty():
				continue
			var entry = entries.get(anim_grh, {})
			var speed: float = entry.get("speed", 100.0) if entry.get("num_frames", 1) > 1 else 100.0
			result.append({
				"label": "FX %d" % fx_idx,
				"grh_index": anim_grh,
				"frames": frames,
				"speed": speed,
				"source": "Fxs.ind"
			})

	return result


## Load the image for a given file_num from the graficos folder.
## Returns null if not found.
func _load_image_for_file_num(fnum: int) -> Image:
	if _graficos_folder_path.is_empty() or fnum <= 0:
		return null
	# Try common extensions
	for ext in ["png", "bmp", "jpg", "jpeg", "webp"]:
		var p := _graficos_folder_path.path_join("%d.%s" % [fnum, ext])
		if FileAccess.file_exists(p):
			return _load_image_from_os_path(p)
	return null


## For each related animation preview, collect its unique file_nums,
## load the images, and pass them as textures dict to the preview.
func _load_related_textures(related: Array, current_file_num: int, current_img: Image) -> void:
	# Cache loaded images to avoid reloading the same file_num
	var img_cache: Dictionary = {}
	img_cache[current_file_num] = current_img

	for i in range(related.size()):
		var anim: Dictionary = related[i]
		var frames: Array = anim.get("frames", [])
		# Collect unique file_nums in this animation
		var textures: Dictionary = {}
		for fr in frames:
			var fnum: int = fr.get("file_num", 0)
			if fnum <= 0:
				continue
			if textures.has(fnum):
				continue
			# Load or use cache
			var img: Image
			if img_cache.has(fnum):
				img = img_cache[fnum]
			else:
				img = _load_image_for_file_num(fnum)
				if img != null:
					img_cache[fnum] = img
			if img != null:
				textures[fnum] = ImageTexture.create_from_image(img)
		if i < _inspector._related_previews.size():
			_inspector._related_previews[i].set_textures(textures)

	# If FX was promoted to main preview, set textures there too
	if _inspector._fx_in_preview and related.size() == 1:
		var frames: Array = related[0].get("frames", [])
		var textures: Dictionary = {}
		for fr in frames:
			var fnum: int = fr.get("file_num", 0)
			if fnum > 0 and img_cache.has(fnum):
				textures[fnum] = ImageTexture.create_from_image(img_cache[fnum])
		_inspector._preview.set_textures(textures)

	# Build a global file_num → texture dict for frame list thumbnails
	var list_textures: Dictionary = {}
	for fnum in img_cache:
		if fnum != current_file_num:
			list_textures[fnum] = ImageTexture.create_from_image(img_cache[fnum])
	_inspector.set_related_textures_for_list(list_textures)
	# Re-render frame list now that textures are available
	_inspector._rebuild_frame_list(_current_frames, _selected_frame_idx)


static func _read_i16(f: FileAccess) -> int:
	var val := f.get_16()
	if val >= 0x8000:
		return val - 0x10000
	return val


func _load_personajes_ind(path: String) -> Array:
	var bodies: Array = []
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return bodies
	if f.get_length() < 265:
		f.close()
		return bodies
	f.seek(263)
	var count: int = _read_i16(f)
	for i in range(1, count + 1):
		bodies.append({
			"index": i,
			"walk_n": _read_i16(f), "walk_e": _read_i16(f),
			"walk_s": _read_i16(f), "walk_w": _read_i16(f),
			"head_x": _read_i16(f), "head_y": _read_i16(f)
		})
	f.close()
	return bodies


func _load_fxs_ind(path: String) -> void:
	_fxs_data.clear()
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return
	if f.get_length() < 265:
		f.close()
		return
	_mi_cabecera_fxs = f.get_buffer(263)
	var count: int = _read_i16(f)
	for i in range(1, count + 1):
		_fxs_data.append({
			"index": i,
			"animacion": _read_i16(f),
			"offset_x": _read_i16(f),
			"offset_y": _read_i16(f)
		})
	f.close()


func _load_image_from_os_path(path: String) -> Image:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return null
	var bytes := f.get_buffer(f.get_length())
	f.close()
	if bytes.is_empty():
		return null

	var img := Image.new()
	var ext := path.get_extension().to_lower()
	var err: Error

	if ext == "png":
		err = img.load_png_from_buffer(bytes)
		if err != OK:
			var clean := _strip_png_metadata(bytes)
			err = img.load_png_from_buffer(clean)
		if err != OK:
			var tmp := "user://tmp_load.png"
			var tf := FileAccess.open(tmp, FileAccess.WRITE)
			if tf != null:
				tf.store_buffer(bytes)
				tf.close()
				img = Image.load_from_file(tmp)
				if img != null:
					return img
			return null
	elif ext in ["jpg", "jpeg"]:
		err = img.load_jpg_from_buffer(bytes)
	elif ext == "bmp":
		err = img.load_bmp_from_buffer(bytes)
	elif ext == "webp":
		err = img.load_webp_from_buffer(bytes)
	else:
		err = img.load_png_from_buffer(bytes)

	if err != OK:
		return null
	return img


func _strip_png_metadata(bytes: PackedByteArray) -> PackedByteArray:
	if bytes.size() < 8:
		return bytes
	var sig := [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
	for i in range(8):
		if bytes[i] != sig[i]:
			return bytes
	var skip_types := ["iCCP", "sRGB", "gAMA", "cHRM", "tEXt", "iTXt", "zTXt"]
	var result := PackedByteArray()
	result.append_array(bytes.slice(0, 8))
	var pos := 8
	while pos + 12 <= bytes.size():
		var length: int = (bytes[pos] << 24) | (bytes[pos+1] << 16) | (bytes[pos+2] << 8) | bytes[pos+3]
		var chunk_end := pos + 12 + length
		if chunk_end > bytes.size():
			break
		var t := ""
		for i in range(4):
			t += char(bytes[pos + 4 + i])
		if t not in skip_types:
			result.append_array(bytes.slice(pos, chunk_end))
		pos = chunk_end
		if t == "IEND":
			break
	return result


# ── Prefs ────────────────────────────────────────────────────────────────────

func _load_prefs() -> void:
	if _prefs.load(PREFS_PATH) != OK:
		return
	_recent_clients = _prefs.get_value("general", "recent_clients", [])


func _push_recent_client(path: String) -> void:
	_recent_clients.erase(path)
	_recent_clients.push_front(path)
	while _recent_clients.size() > MAX_RECENT:
		_recent_clients.pop_back()
	_save_prefs()


# ── Session save/restore ─────────────────────────────────────────────────────

func _save_session() -> void:
	# Save current session state so the app reopens where you left off
	_prefs.set_value("general", "recent_clients", _recent_clients)

	# Last open folder
	if not _client_graficos_path.is_empty():
		_prefs.set_value("session", "last_client_path", _client_graficos_path)

	# Last selected file
	if not _current_image_path.is_empty():
		_prefs.set_value("session", "last_image_path", _current_image_path)
		_prefs.set_value("session", "last_file_num", _current_file_num)

	# Detect + grid state
	_prefs.set_value("session", "detect_enabled", _canvas.detect_enabled)
	_prefs.set_value("session", "show_grid", _canvas.show_grid)
	_prefs.set_value("session", "grid_cell_w", _canvas.grid_cell_w)
	_prefs.set_value("session", "grid_cell_h", _canvas.grid_cell_h)
	_prefs.set_value("session", "grid_line_w", _canvas.grid_line_width)
	_prefs.set_value("session", "grid_color", _canvas.grid_color.to_html(false))

	# Tool mode
	_prefs.set_value("session", "tool_mode", _canvas.tool_mode)

	# Inspector tab
	if _inspector._tabs != null:
		_prefs.set_value("session", "inspector_tab", _inspector._tabs.current_tab)

	# Next GRH index — no longer saved; always derived from .ind on load

	_prefs.save(PREFS_PATH)


func _restore_session() -> void:
	if _prefs.load(PREFS_PATH) != OK:
		return

	# Restore client folder
	var last_client: String = _prefs.get_value("session", "last_client_path", "")

	if not last_client.is_empty() and DirAccess.dir_exists_absolute(last_client):
		_load_client_folder(last_client)
	else:
		if not last_client.is_empty():
			_update_status("Carpeta anterior no encontrada. Abre una nueva.")
		return

	# Restore detect + grid
	var detect_on: bool = _prefs.get_value("session", "detect_enabled", false)
	_toolbar.set_detect(detect_on)
	_canvas.set_detect(detect_on)
	var grid_on: bool = _prefs.get_value("session", "show_grid", true)
	var gcw: int = _prefs.get_value("session", "grid_cell_w", 128)
	var gch: int = _prefs.get_value("session", "grid_cell_h", 128)
	var glw: float = _prefs.get_value("session", "grid_line_w", 1.0)
	var gc_hex: String = _prefs.get_value("session", "grid_color", "ffd900")
	var gcol := Color.from_string(gc_hex, Color(1.0, 0.85, 0.0))
	_toolbar.set_grid(grid_on, gcw, gch, glw, gcol)
	_canvas.set_grid_visible(grid_on)
	_canvas.set_grid_cell(gcw, gch)
	_canvas.set_grid_line_w(glw)
	_canvas.set_grid_color(gcol)

	# Restore tool mode (migrate old 0=Select,1=Draw,2=Pan → 0=Edit,1=Pan)
	var tool_mode: int = _prefs.get_value("session", "tool_mode", 0)
	if tool_mode >= 2:
		tool_mode = 1  # Pan
	else:
		tool_mode = 0  # Edit
	_toolbar.set_tool(tool_mode)

	# Restore inspector tab
	var tab_idx: int = _prefs.get_value("session", "inspector_tab", 0)
	if _inspector._tabs != null and tab_idx < _inspector._tabs.get_tab_count():
		_inspector._tabs.current_tab = tab_idx

	# Next GRH: always use what was loaded from the .ind file (max_index + 1).
	# Session-saved value is stale if the .ind was modified externally.
	_inspector.set_next_grh(_next_grh_index)

	# Restore last selected image
	var last_image: String = _prefs.get_value("session", "last_image_path", "")
	if not last_image.is_empty() and FileAccess.file_exists(last_image):
		var last_fnum: int = _prefs.get_value("session", "last_file_num", 0)
		# Defer so the file list has finished loading
		call_deferred("_on_file_selected", last_image, last_fnum)


func _update_status(msg: String) -> void:
	if _lbl_status != null:
		_lbl_status.text = msg
	print(msg)


# ── Texture indexing ─────────────────────────────────────────────────────────

func _parse_indices_categories(lines: PackedStringArray) -> PackedStringArray:
	var cats: Dictionary = {}
	for line in lines:
		var stripped := line.strip_edges()
		if stripped.begins_with("Type="):
			var val := stripped.substr(5).strip_edges()
			if not val.is_empty():
				cats[val] = true
	var result: PackedStringArray = []
	# Preferred order first
	for pref in ["Terreno", "Dungeons", "Techos", "Estructuras", "Naturaleza", "Objetos", "Otros"]:
		if cats.has(pref):
			result.append(pref)
			cats.erase(pref)
	for key in cats:
		result.append(key)
	return result


func _on_canvas_context_menu(idx: int, screen_pos: Vector2) -> void:
	if idx < 0 or idx >= _current_frames.size():
		return
	_ctx_frame_idx = idx
	_ctx_menu.position = Vector2i(screen_pos)
	_ctx_menu.popup()


func _on_ctx_menu_item(id: int) -> void:
	if id == 0 and _ctx_frame_idx >= 0 and _ctx_frame_idx < _current_frames.size():
		_open_texture_index_dialog(_ctx_frame_idx)


func _on_texture_split_requested(frame: Dictionary, tiles_w: int, tiles_h: int) -> void:
	# Split a large frame into NxM 32x32 tiles with GRH indices
	var sx: int = frame.get("sx", 0)
	var sy: int = frame.get("sy", 0)
	var fw: int = frame.get("w", 0)
	var fh: int = frame.get("h", 0)
	var file_num: int = frame.get("file_num", _current_file_num)

	_push_undo()

	# Remove the original frame
	var orig_idx := -1
	for i in range(_current_frames.size()):
		var f: Dictionary = _current_frames[i]
		if f.get("sx", -1) == sx and f.get("sy", -1) == sy and f.get("w", -1) == fw and f.get("h", -1) == fh:
			orig_idx = i
			break
	if orig_idx >= 0:
		_current_frames.remove_at(orig_idx)

	# Create NxM 32x32 sub-frames with consecutive GRH indices
	var first_grh := _next_grh_index
	_pending_tex_first_grh = first_grh
	for row in range(tiles_h):
		for col in range(tiles_w):
			var grh_idx := _next_grh_index
			_next_grh_index += 1
			var tile_sx := sx + col * 32
			var tile_sy := sy + row * 32
			var frame_dict := {
				"sx": tile_sx, "sy": tile_sy, "w": 32, "h": 32,
				"grh_index": grh_idx, "file_num": file_num
			}
			_current_frames.append(frame_dict)
			_grh_data["entries"][grh_idx] = {
				"grh_index": grh_idx, "num_frames": 1,
				"file_num": file_num, "sx": tile_sx, "sy": tile_sy, "w": 32, "h": 32
			}

	if _next_grh_index - 1 > _grh_data["max_index"]:
		_grh_data["max_index"] = _next_grh_index - 1
	_inspector.set_next_grh(_next_grh_index)
	_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())

	var total := tiles_w * tiles_h
	_dirty = true
	_selected_frame_idx = -1
	_refresh_all()
	_update_status("Frame dividido: %d×%d = %d tiles (G%d–G%d)" % [tiles_w, tiles_h, total, first_grh, first_grh + total - 1])


func _open_texture_index_dialog(idx: int) -> void:
	if _init_folder.is_empty():
		_update_status("Abrí una carpeta de cliente primero (necesita INIT/).")
		return
	var frame: Dictionary = _current_frames[idx]
	if not _tex_index_dialog.is_inside_tree():
		add_child(_tex_index_dialog)
	_tex_index_dialog.open_with_frame(frame, _current_texture, _indices_categories)


# Pending texture index data (between dialog confirm and user confirmation)
var _pending_tex_name: String = ""
var _pending_tex_category: String = ""
var _pending_tex_capa: int = 0
var _pending_tex_first_grh: int = -1  # set by split_requested for NxM

func _on_texture_index_confirmed(tex_name: String, category: String, capa: int) -> void:
	_pending_tex_name = tex_name
	_pending_tex_category = category
	_pending_tex_capa = capa
	_on_texture_index_final()


func _ensure_confirm_dialog() -> void:
	if _dlg_save_confirm != null:
		return
	_dlg_save_confirm = Window.new()
	_dlg_save_confirm.title = "Guardar cambios"
	_dlg_save_confirm.size = Vector2i(520, 400)
	_dlg_save_confirm.exclusive = true
	_dlg_save_confirm.wrap_controls = true
	_dlg_save_confirm.close_requested.connect(func(): _dlg_save_confirm.hide())
	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	_dlg_save_confirm.add_child(margin)
	var vb := VBoxContainer.new()
	vb.add_theme_constant_override("separation", 8)
	margin.add_child(vb)
	_index_preview_label_save = RichTextLabel.new()
	_index_preview_label_save.bbcode_enabled = true
	_index_preview_label_save.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_index_preview_label_save.scroll_following = false
	vb.add_child(_index_preview_label_save)
	var btn_row := HBoxContainer.new()
	btn_row.alignment = BoxContainer.ALIGNMENT_END
	btn_row.add_theme_constant_override("separation", 8)
	vb.add_child(btn_row)
	var btn_cancel := Button.new()
	btn_cancel.text = "Cancelar"
	btn_cancel.pressed.connect(func(): _dlg_save_confirm.hide())
	btn_row.add_child(btn_cancel)
	var btn_save := Button.new()
	btn_save.text = "Guardar todo"
	btn_save.pressed.connect(_on_save_confirmed)
	btn_row.add_child(btn_save)


func _on_texture_index_final() -> void:
	var tiles := _tex_index_dialog.get_tiles()
	var source := _tex_index_dialog.get_source_frame()
	var sx: int = source.get("sx", 0)
	var sy: int = source.get("sy", 0)
	var fw: int = source.get("w", 0)
	var fh: int = source.get("h", 0)
	var file_num: int = source.get("file_num", _current_file_num)
	var tw: int = tiles.x
	var th: int = tiles.y
	var is_single := (tw == 1 and th == 1)
	var first_grh: int
	var total: int

	if is_single:
		# 1x1: create indexed frame now (no prior split)
		first_grh = _next_grh_index
		_push_undo()
		# Remove original
		for i in range(_current_frames.size()):
			var f: Dictionary = _current_frames[i]
			if f.get("sx", -1) == sx and f.get("sy", -1) == sy and f.get("w", -1) == fw and f.get("h", -1) == fh:
				_current_frames.remove_at(i)
				break
		var grh_idx := _next_grh_index
		_next_grh_index += 1
		_current_frames.append({"sx": sx, "sy": sy, "w": fw, "h": fh, "grh_index": grh_idx, "file_num": file_num})
		_grh_data["entries"][grh_idx] = {
			"grh_index": grh_idx, "num_frames": 1,
			"file_num": file_num, "sx": sx, "sy": sy, "w": fw, "h": fh
		}
		if grh_idx > _grh_data["max_index"]:
			_grh_data["max_index"] = grh_idx
		_inspector.set_next_grh(_next_grh_index)
		_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
		total = 1
	else:
		# NxM: GRHs already assigned in _on_texture_split_requested — just persist
		first_grh = _pending_tex_first_grh
		total = tw * th

	# Save indices.ini entry IN MEMORY (no disk write)
	_append_indices_ini_entry(_pending_tex_name, first_grh, tw, th, _pending_tex_capa, _pending_tex_category)

	_dirty = true
	_update_status("Textura '%s' indexada en memoria: %d GRHs (G%d–G%d). Presiona GUARDAR para escribir a disco." % [_pending_tex_name, total, first_grh, first_grh + total - 1])
	_selected_frame_idx = -1
	_refresh_all()


func _append_indices_ini_entry(tex_name: String, grh_index: int, ancho: int, alto: int, capa: int, category: String) -> void:
	# Memory-only — updates _indices_ini_data, does NOT write to disk
	# Find current ref count
	var ref_count: int = 0
	var ref_line_idx: int = -1
	for i in range(_indices_ini_data.size()):
		var line := _indices_ini_data[i].strip_edges()
		if line.begins_with("Referencias="):
			ref_count = int(line.substr(12))
			ref_line_idx = i
			break

	var new_ref := ref_count + 1

	# Update count
	if ref_line_idx >= 0:
		_indices_ini_data[ref_line_idx] = "Referencias=%d" % new_ref
	else:
		var header: PackedStringArray = ["[INIT]", "Referencias=%d" % new_ref, ""]
		_indices_ini_data = header + _indices_ini_data

	# Append new section
	_indices_ini_data.append("")
	_indices_ini_data.append("[REFERENCIA%d]" % new_ref)
	_indices_ini_data.append("Nombre=%s" % tex_name)
	_indices_ini_data.append("GrhIndice=%d" % grh_index)
	_indices_ini_data.append("Ancho=%d" % ancho)
	_indices_ini_data.append("Alto=%d" % alto)
	_indices_ini_data.append("Capa=%d" % capa)
	_indices_ini_data.append("Type=%s" % category)

	# Update category list
	if not _indices_categories.has(category):
		_indices_categories.append(category)


# ── Asset save handlers ──────────────────────────────────────────────────────

const HEADER_MAGIC := "Argentum Online by Noland-Studios."

func _get_init_path(filename: String) -> String:
	if _init_folder.is_empty():
		return ""
	return _init_folder.path_join(filename)


func _read_binary_ind(path: String) -> PackedByteArray:
	if not FileAccess.file_exists(path):
		return PackedByteArray()
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return PackedByteArray()
	var data := f.get_buffer(f.get_length())
	f.close()
	return data


func _write_binary_ind(path: String, data: PackedByteArray) -> bool:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("Error al escribir: " + path)
		return false
	f.store_buffer(data)
	f.close()
	return true


func _append_binary_entry(filename: String, entry_bytes: PackedByteArray, entry_size: int) -> void:
	var path := _get_init_path(filename)
	if path.is_empty():
		_update_status("Carpeta INIT no configurada. Abrí una carpeta de cliente primero.")
		return

	var data := _read_binary_ind(path)
	var new_index: int

	if data.size() == 0:
		# Create new file: 263-byte header + 2-byte count + entry
		var header := PackedByteArray()
		header.resize(263)
		var magic := HEADER_MAGIC.to_ascii_buffer()
		for i in range(mini(magic.size(), 263)):
			header[i] = magic[i]
		header.append(1)  # count low byte
		header.append(0)  # count high byte
		header.append_array(entry_bytes)
		data = header
		new_index = 1
	else:
		# Read existing count at offset 263
		if data.size() < 265:
			_update_status("Archivo corrupto: " + filename)
			return
		var count: int = data[263] | (data[264] << 8)
		new_index = count + 1
		# Update count
		data[263] = new_index & 0xFF
		data[264] = (new_index >> 8) & 0xFF
		# Append entry
		data.append_array(entry_bytes)

	if _write_binary_ind(path, data):
		_update_status("%s: entrada #%d guardada → %s" % [filename, new_index, path.get_file()])


func _append_ini_entry(filename: String, section_prefix: String, count_key: String, fields: Dictionary) -> void:
	var path := _get_init_path(filename)
	if path.is_empty():
		_update_status("Carpeta INIT no configurada. Abrí una carpeta de cliente primero.")
		return

	var lines: PackedStringArray
	var count: int = 0

	if FileAccess.file_exists(path):
		var f := FileAccess.open(path, FileAccess.READ)
		if f != null:
			lines = f.get_as_text().split("\n")
			f.close()
		# Parse current count
		for line in lines:
			if line.strip_edges().begins_with(count_key + "="):
				count = int(line.strip_edges().substr(count_key.length() + 1))
				break
	else:
		lines = PackedStringArray(["[INIT]", count_key + "=0", ""])

	var new_index := count + 1

	# Update count in [INIT]
	for i in range(lines.size()):
		if lines[i].strip_edges().begins_with(count_key + "="):
			lines[i] = count_key + "=" + str(new_index)
			break

	# Append new section
	lines.append("")
	lines.append("[%s%d]" % [section_prefix, new_index])
	for key in fields:
		lines.append("%s=%s" % [key, str(fields[key])])

	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("Error al escribir: " + path)
		return
	f.store_string("\n".join(lines))
	f.close()
	_update_status("%s: %s%d guardado → %s" % [filename, section_prefix, new_index, path.get_file()])


func _on_save_body(walk_n: int, walk_e: int, walk_s: int, walk_w: int, off_x: int, off_y: int) -> void:
	# personajes.ind — binary, 12 bytes per entry
	var entry := PackedByteArray()
	entry.resize(12)
	entry.encode_s16(0, walk_n)
	entry.encode_s16(2, walk_e)
	entry.encode_s16(4, walk_s)
	entry.encode_s16(6, walk_w)
	entry.encode_s16(8, off_x)
	entry.encode_s16(10, off_y)
	_append_binary_entry("Personajes.ind", entry, 12)


func _on_save_head(head_n: int, head_e: int, head_s: int, head_w: int) -> void:
	# cabezas.ind — binary, 8 bytes per entry
	var entry := PackedByteArray()
	entry.resize(8)
	entry.encode_s16(0, head_n)
	entry.encode_s16(2, head_e)
	entry.encode_s16(4, head_s)
	entry.encode_s16(6, head_w)
	_append_binary_entry("Cabezas.ind", entry, 8)


func _on_save_helmet(head_n: int, head_e: int, head_s: int, head_w: int) -> void:
	# cascos.ind — binary, 8 bytes per entry
	var entry := PackedByteArray()
	entry.resize(8)
	entry.encode_s16(0, head_n)
	entry.encode_s16(2, head_e)
	entry.encode_s16(4, head_s)
	entry.encode_s16(6, head_w)
	_append_binary_entry("Cascos.ind", entry, 8)


func _on_save_fx(anim_grh: int, off_x: int, off_y: int) -> void:
	# FXs.ind — binary, 6 bytes per entry
	var entry := PackedByteArray()
	entry.resize(6)
	entry.encode_s16(0, anim_grh)
	entry.encode_s16(2, off_x)
	entry.encode_s16(4, off_y)
	_append_binary_entry("FXs.ind", entry, 6)


func _on_save_weapon(dir_n: int, dir_e: int, dir_s: int, dir_w: int) -> void:
	# armas.dat — INI text
	_append_ini_entry("Armas.dat", "Arma", "NumArmas", {
		"Dir1": dir_n, "Dir2": dir_e, "Dir3": dir_s, "Dir4": dir_w
	})


func _on_save_shield(dir_n: int, dir_e: int, dir_s: int, dir_w: int) -> void:
	# escudos.dat — INI text
	_append_ini_entry("Escudos.dat", "ESC", "NumEscudos", {
		"Dir1": dir_n, "Dir2": dir_e, "Dir3": dir_s, "Dir4": dir_w
	})
