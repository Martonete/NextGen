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

# Undo stack: stores snapshots of {frames, selected, next_grh}
const MAX_UNDO := 50
var _undo_stack: Array = []
var _redo_stack: Array = []

# ── UI components ────────────────────────────────────────────────────────────

var _toolbar: IndexerToolBar
var _file_list: FileListPanel
var _canvas: SpriteCanvas
var _inspector: InspectorPanel
var _lbl_status: Label
var _menu_ver: PopupMenu

# Dialogs
var _dlg_client_folder: FileDialog
var _dlg_save_ind: FileDialog

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
	# Default snap: Potencia de 2 per dimension (mode 2)
	_canvas.set_snap(2, 32, 32)
	_update_status("Listo. Abre una carpeta de cliente para comenzar.")
	# Restore previous session
	call_deferred("_restore_session")


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_save_session()
		get_tree().quit()


func _process(delta: float) -> void:
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


func _connect_signals() -> void:
	# File list
	_file_list.file_selected.connect(_on_file_selected)

	# Toolbar
	_toolbar.tool_changed.connect(_on_tool_changed)
	_toolbar.snap_changed.connect(func(mode, sx, sy): _canvas.set_snap(mode, sx, sy))
	_toolbar.zoom_in_pressed.connect(func(): _canvas.zoom_in())
	_toolbar.zoom_out_pressed.connect(func(): _canvas.zoom_out())
	_toolbar.zoom_fit_pressed.connect(func(): _canvas.fit_to_canvas())
	_toolbar.zoom_reset_pressed.connect(func(): _canvas.zoom_reset())
	_toolbar.save_pressed.connect(_on_save_ind)
	_toolbar.index_pressed.connect(_on_index_image)

	# Canvas
	_canvas.frame_drawn.connect(_on_canvas_frame_drawn)
	_canvas.frame_selected.connect(_on_canvas_frame_selected)
	_canvas.blob_clicked.connect(_on_canvas_blob_clicked)
	_canvas.frame_resized.connect(_on_canvas_frame_resized)
	_canvas.frame_delete_pressed.connect(func(idx): _delete_frame(idx))

	# Inspector
	_inspector.frame_selected.connect(_on_inspector_frame_selected)
	_inspector.frame_deleted.connect(_on_inspector_frame_deleted)
	_inspector.frame_props_changed.connect(_on_inspector_props_changed)
	_inspector.clear_frames_pressed.connect(_on_clear_frames)
	_inspector.detect_grid_pressed.connect(_on_detect_grid)
	_inspector.detect_blobs_pressed.connect(_on_detect_blobs)
	_inspector.create_anim_pressed.connect(_on_create_anim_grh)
	_inspector.split_frame_pressed.connect(_on_split_frame)
	_inspector.save_init_pressed.connect(_on_save_init_file)
	_inspector.add_manual_frame_pressed.connect(_on_add_manual_frame)
	_inspector.index_frame_pressed.connect(_on_index_single_frame)
	_inspector.view_file_num_pressed.connect(_on_view_file_num)
	_inspector.next_grh_changed.connect(func(v): _next_grh_index = v)


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
			KEY_V: _toolbar.set_tool(IndexerToolBar.Tool.SELECT)
			KEY_R, KEY_D: _toolbar.set_tool(IndexerToolBar.Tool.DRAW)
			KEY_H: _toolbar.set_tool(IndexerToolBar.Tool.PAN)


# ── Tool mode ────────────────────────────────────────────────────────────────

func _on_tool_changed(mode: int) -> void:
	_canvas.tool_mode = mode


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

	var img := _load_image_from_os_path(path)
	if img == null:
		_update_status("No se pudo cargar: " + path)
		return

	_current_image = img
	_current_texture = ImageTexture.create_from_image(img)
	_canvas.load_image(img)
	_inspector.set_image(img)

	# Load existing GRHs for this file
	if _grh_data["entries"].size() > 0 and file_num > 0:
		var entries := _get_grh_entries_for_file_num(file_num)
		for e in entries:
			_current_frames.append({
				"sx": e.get("sx", 0), "sy": e.get("sy", 0),
				"w": e.get("width", 32), "h": e.get("height", 32),
				"grh_index": e.grh_index, "file_num": file_num
			})

	_refresh_all()

	_update_status("%s — FileNum=%d — %dx%d px (analizando blobs...)" % [
		path.get_file(), file_num, img.get_width(), img.get_height()])

	# Pre-compute blob map
	var blob_data := FrameDetector.detect_blobs_indexed(img, 0.03, 3, 1)
	_canvas.set_blob_data(blob_data["map"], blob_data["rects"], blob_data["id_to_rect"], blob_data["width"])

	# Pre-compute content regions for smart mode
	var content_regions := FrameDetector.detect_content_rows(img, 0.03, 3, 1)
	_canvas.set_content_regions(content_regions)

	# Auto-detect sprite sheet type and suggest best snap mode
	var rects: Array = blob_data["rects"]
	var snap_hint := _detect_snap_hint(rects, content_regions)

	# Update GRH viewer
	var grh_entries := _get_grh_entries_for_file_num(file_num)
	_inspector.update_grh_viewer(grh_entries, _current_texture)

	# Detect related animations (bodies, FXs) that use GRHs from this image
	var related := _find_related_animations(file_num)
	_inspector.update_related_animations(related)
	# Collect all related frames for the unified frame list
	var all_related_frames: Array = []
	for anim in related:
		for fr in anim.get("frames", []):
			all_related_frames.append(fr)
	_inspector.set_related_frames(all_related_frames)
	# Load per-file_num textures for each related animation preview + frame list thumbnails
	_load_related_textures(related, file_num, img)

	var related_info := ""
	if not related.is_empty():
		related_info = " — %d animaciones" % related.size()

	_update_status("%s — FileNum=%d — %d GRHs — %d blobs — %s%s" % [
		path.get_file(), file_num, _current_frames.size(), rects.size(), snap_hint, related_info])


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
	if _current_image == null: return maxi(x, 0)
	return clampi(x, 0, _current_image.get_width() - 1)

func _clamp_y(y: int) -> int:
	if _current_image == null: return maxi(y, 0)
	return clampi(y, 0, _current_image.get_height() - 1)

func _clamp_w(x: int, w: int) -> int:
	if _current_image == null: return maxi(w, 1)
	return clampi(w, 1, _current_image.get_width() - x)

func _clamp_h(y: int, h: int) -> int:
	if _current_image == null: return maxi(h, 1)
	return clampi(h, 1, _current_image.get_height() - y)


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

	if ratio >= 0.5 and best["count"] >= 3:
		# Majority of blobs share a common size → grid mode
		var gw: int = best["w"]
		var gh: int = best["h"]
		_toolbar.set_snap(1, gw, gh)
		_canvas.set_snap(1, gw, gh)
		return "Snap: Grid %dx%d (auto — %d/%d blobs iguales)" % [gw, gh, best["count"], sizes.size()]
	elif content_regions.size() >= 2 and content_regions.size() <= rects.size() + 2:
		# Multiple distinct content regions detected → Smart mode
		# (content_regions merges overlapping blobs into logical groups)
		_toolbar.set_snap(4)
		_canvas.set_snap(4, 32, 32)
		return "Snap: Smart (auto — %d regiones de contenido)" % content_regions.size()
	elif w_is_pow2 and h_is_pow2:
		# Mixed sizes but pow2 works
		_toolbar.set_snap(2)
		_canvas.set_snap(2, 32, 32)
		return "Snap: Pot.2 (auto — tamaños variados)"
	else:
		# Mixed sizes, not all pow2 → Smart if we have regions
		if content_regions.size() >= 2:
			_toolbar.set_snap(4)
			_canvas.set_snap(4, 32, 32)
			return "Snap: Smart (auto — %d regiones)" % content_regions.size()
		_toolbar.set_snap(0)
		_canvas.set_snap(0, 32, 32)
		return "Snap: Off (auto — tamaños irregulares)"


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
	var added := 0
	for frame in _current_frames:
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
		added += 1
	_inspector.set_grh_data(_grh_data["max_index"], _grh_data["entries"].size())
	_refresh_all()
	_update_status("Indexados %d frames. Total: %d entradas." % [added, _grh_data["entries"].size()])


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
	if _ind_path.is_empty():
		_dlg_save_ind.popup_centered_ratio(0.7)
	else:
		_save_ind_to_path(_ind_path)


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


func _load_personajes_ind(path: String) -> Array:
	var bodies: Array = []
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return bodies
	if f.get_length() < 265:
		f.close()
		return bodies
	f.seek(263)
	var count: int = f.get_16()
	for i in range(1, count + 1):
		bodies.append({
			"index": i,
			"walk_n": f.get_16(), "walk_e": f.get_16(),
			"walk_s": f.get_16(), "walk_w": f.get_16(),
			"head_x": f.get_16(), "head_y": f.get_16()
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
	var count: int = f.get_16()
	for i in range(1, count + 1):
		_fxs_data.append({
			"index": i,
			"animacion": f.get_16(),
			"offset_x": f.get_16(),
			"offset_y": f.get_16()
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


func _save_prefs() -> void:
	_prefs.set_value("general", "recent_clients", _recent_clients)
	_prefs.save(PREFS_PATH)


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

	# Snap state
	_prefs.set_value("session", "snap_mode", _canvas.snap_mode)
	_prefs.set_value("session", "snap_x", _canvas.snap_x)
	_prefs.set_value("session", "snap_y", _canvas.snap_y)

	# Tool mode
	_prefs.set_value("session", "tool_mode", _canvas.tool_mode)

	# Inspector tab
	if _inspector._tabs != null:
		_prefs.set_value("session", "inspector_tab", _inspector._tabs.current_tab)

	# Next GRH index
	_prefs.set_value("session", "next_grh", _next_grh_index)

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

	# Restore snap
	var snap_mode: int = _prefs.get_value("session", "snap_mode", 2)
	var snap_sx: int = _prefs.get_value("session", "snap_x", 32)
	var snap_sy: int = _prefs.get_value("session", "snap_y", 32)
	_toolbar.set_snap(snap_mode, snap_sx, snap_sy)
	_canvas.set_snap(snap_mode, snap_sx, snap_sy)

	# Restore tool mode
	var tool_mode: int = _prefs.get_value("session", "tool_mode", 0)
	_toolbar.set_tool(tool_mode)

	# Restore inspector tab
	var tab_idx: int = _prefs.get_value("session", "inspector_tab", 0)
	if _inspector._tabs != null and tab_idx < _inspector._tabs.get_tab_count():
		_inspector._tabs.current_tab = tab_idx

	# Restore next GRH (only if higher than what was loaded from .ind)
	var saved_next: int = _prefs.get_value("session", "next_grh", 0)
	if saved_next > _next_grh_index:
		_next_grh_index = saved_next
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
