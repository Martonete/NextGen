## Main.gd — Ruthnar Sprite Indexer
## Herramienta para indexar sprite sheets al formato Graficos.ind de Argentum Online

extends Control

# ── Estado ───────────────────────────────────────────────────────────────────

var _grh_data: Dictionary = { "version": 12, "max_index": 0, "entries": {} }
var _ind_path: String = ""           # Ruta del Graficos.ind cargado

var _source_folder: String = ""      # Carpeta de imágenes fuente
var _image_files: Array[String] = [] # Lista de archivos en la carpeta
var _current_image_path: String = "" # Imagen actualmente seleccionada
var _current_image: Image = null     # Imagen cargada

# Frames definidos para la imagen actual (pendientes de indexar)
# Array de {sx, sy, w, h, grh_index, file_num}
var _current_frames: Array = []
var _selected_frame_idx: int = -1

var _next_grh_index: int = 1         # Próximo GrhIndex a asignar
var _filenum_base: int = 1           # FileNum = _filenum_base + NNN (de gfx_NNN.png)
var _client_graficos_path: String = "" # Carpeta Graficos del cliente (para copiar)

# ── Referencias a controles UI ───────────────────────────────────────────────

var _lbl_max_grh: Label
var _lbl_status: Label
var _file_list: ItemList
var _canvas: SpriteCanvas

var _spin_next_grh: SpinBox
var _spin_filenum_base: SpinBox

# Grid detection
var _spin_cell_w: SpinBox
var _spin_cell_h: SpinBox
var _spin_off_x: SpinBox
var _spin_off_y: SpinBox
var _spin_margin_x: SpinBox
var _spin_margin_y: SpinBox
var _chk_skip_empty: CheckButton
var _spin_alpha_thresh: SpinBox
var _spin_min_size: SpinBox
var _spin_padding: SpinBox

# Split frame
var _spin_split_w: SpinBox
var _spin_split_h: SpinBox

# Snap
var _snap_mode: int = 0
var _spin_snap_x: SpinBox
var _spin_snap_y: SpinBox
var _snap_multiple_row: Control  # row shown only in MULTIPLE mode

# Animation / preview
var _preview: FramePreviewPanel
var _btn_play: Button
var _lbl_anim_frame: Label
var _spin_anim_fps: SpinBox
var _anim_playing: bool = false
var _anim_time: float = 0.0
var _anim_fps: float = 8.0
var _anim_frame_idx: int = 0

# Frame list (VBox with per-row delete buttons)
var _frame_list_vbox: VBoxContainer

# Thumbnail lazy-loading queue
var _thumb_queue: Array[int] = []

# Frame props
var _spin_sx: SpinBox
var _spin_sy: SpinBox
var _spin_fw: SpinBox
var _spin_fh: SpinBox
var _spin_grh_override: SpinBox
var _lbl_frame_info: Label

# Dialogs
var _file_dialog_ind: FileDialog
var _file_dialog_save: FileDialog
var _file_dialog_folder: FileDialog
var _file_dialog_client: FileDialog
var _file_dialog_init: FileDialog
var _file_dialog_client_folder: FileDialog

# INIT folder data
var _init_folder: String = ""
var _bodies_data: Array = []   # cargado de Personajes.ind si existe

# Client folder mode
var _using_client: bool = false
var _graficos_folder_path: String = ""
var _current_file_num: int = 0
var _current_texture: ImageTexture = null
var _mi_cabecera_personajes: PackedByteArray = PackedByteArray()
var _mi_cabecera_fxs: PackedByteArray = PackedByteArray()

# Fxs data
var _fxs_data: Array = []   # [{index, animacion, offset_x, offset_y}]

# Animated GRH creator
var _edit_anim_indices: LineEdit
var _spin_anim_speed: SpinBox

# Info panel (right-most)
var _init_file_list: ItemList
var _init_text_edit: TextEdit
var _init_current_file_path: String = ""
var _grh_viewer_vbox: VBoxContainer
var _lbl_grh_count: Label

# INIT file list
var _init_files: Array[String] = []

# Floating tool windows
var _m_ver: PopupMenu
var _win_preview: Window
var _win_frames: Window
var _win_frame_props: Window
var _win_split: Window
var _win_autodetect: Window
var _win_snap: Window
var _win_anim_creator: Window
var _win_init_files: Window
var _win_grh_viewer: Window

# Preferencias persistentes
const PREFS_PATH := "user://indexer_prefs.cfg"
const MAX_RECENT := 8
var _prefs: ConfigFile
var _recent_clients: Array = []   # Array[String]
var _m_archivo: PopupMenu         # referencia para actualizar Recientes


# ── Inicialización ────────────────────────────────────────────────────────────

func _ready() -> void:
	_prefs = ConfigFile.new()
	_load_prefs()
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_MAXIMIZED)
	_build_ui()
	_on_snap_changed()
	_apply_prefs_to_dialogs()
	_update_status("Listo. Carga un Graficos.ind y luego una carpeta de imágenes.")


func _process(delta: float) -> void:
	if not _anim_playing or _current_frames.size() <= 1:
		return
	_anim_time += delta
	var frame_duration := 1.0 / maxf(_anim_fps, 1.0)
	if _anim_time >= frame_duration:
		_anim_time = fmod(_anim_time, frame_duration)
		_anim_frame_idx = (_anim_frame_idx + 1) % _current_frames.size()
		_preview.show_frame(_anim_frame_idx)
		_update_anim_label()

	# Carga lazy de miniaturas: 5 por frame para no bloquear la UI
	var processed := 0
	while not _thumb_queue.is_empty() and processed < 5:
		_load_thumb_for(_thumb_queue.pop_front())
		processed += 1


# ── Preferencias persistentes ─────────────────────────────────────────────────

func _load_prefs() -> void:
	if _prefs.load(PREFS_PATH) != OK:
		return
	_recent_clients = _prefs.get_value("general", "recent_clients", [])

func _save_prefs() -> void:
	_prefs.set_value("general", "recent_clients", _recent_clients)
	_prefs.save(PREFS_PATH)

func _push_recent_client(path: String) -> void:
	_recent_clients.erase(path)          # quitar duplicado si existe
	_recent_clients.push_front(path)
	while _recent_clients.size() > MAX_RECENT:
		_recent_clients.pop_back()
	_save_prefs()
	_rebuild_recientes_menu()

func _apply_prefs_to_dialogs() -> void:
	if _recent_clients.is_empty():
		return
	var last: String = _recent_clients[0]
	if DirAccess.dir_exists_absolute(last):
		_file_dialog_client_folder.current_dir = last
		_file_dialog_init.current_dir = last
		_file_dialog_ind.current_dir = last
		_file_dialog_folder.current_dir = last

func _rebuild_recientes_menu() -> void:
	if _m_archivo == null:
		return
	# Estructura fija: idx 0..4 = Abrir cliente / Abrir imgs / sep / "Recientes:" / sep
	# Todo a partir de idx 5 son dinámicos (recientes + sep + Salir)
	while _m_archivo.item_count > 5:
		_m_archivo.remove_item(5)
	for i in range(_recent_clients.size()):
		var p: String = _recent_clients[i]
		# Mostrar las últimas 2 partes de la ruta para que sea legible
		var parts := p.rstrip("/\\").split("/")
		var short := p if parts.size() < 2 else parts[-2].path_join(parts[-1])
		_m_archivo.add_item("  " + short, 200 + i)
	_m_archivo.add_separator()
	_m_archivo.add_item("Salir", 99)

# ── Construcción de UI ────────────────────────────────────────────────────────

func _build_ui() -> void:
	var root := VBoxContainer.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_theme_constant_override("separation", 0)
	add_child(root)

	root.add_child(_build_menu_bar())
	root.add_child(_build_toolbar())

	# 2-panel layout: Left | Center  (tools as floating windows)
	var outer := HSplitContainer.new()
	outer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	outer.split_offset = 230
	root.add_child(outer)

	outer.add_child(_build_left_panel())
	outer.add_child(_build_center_panel())

	root.add_child(_build_statusbar())
	_build_dialogs()
	_rebuild_recientes_menu()
	# Diferir creación de ventanas flotantes para que el OS ya tenga
	# la ventana principal posicionada y los títulos no queden cortados
	call_deferred("_build_tool_windows")


func _build_menu_bar() -> MenuBar:
	var mb := MenuBar.new()

	_m_archivo = PopupMenu.new()
	_m_archivo.name = "Archivo"
	_m_archivo.add_item("Abrir carpeta Cliente...", 0)
	_m_archivo.add_item("Abrir carpeta de imágenes...", 1)
	_m_archivo.add_separator()
	_m_archivo.add_item("Recientes:", -1)   # header no-op
	_m_archivo.set_item_disabled(_m_archivo.item_count - 1, true)
	_m_archivo.add_separator()
	# los ítems recientes se agregan en _rebuild_recientes_menu()
	_m_archivo.id_pressed.connect(_on_archivo_menu)
	mb.add_child(_m_archivo)
	var m_archivo := _m_archivo  # alias local para claridad

	var m_guardar := PopupMenu.new()
	m_guardar.name = "Guardar"
	m_guardar.add_item("Guardar Graficos.ind", 0)
	m_guardar.add_item("Guardar Personajes.ind", 1)
	m_guardar.add_item("Guardar Fxs.ind", 2)
	m_guardar.add_separator()
	m_guardar.add_item("Guardar archivo INIT activo", 10)
	m_guardar.id_pressed.connect(_on_guardar_menu)
	mb.add_child(m_guardar)

	_m_ver = PopupMenu.new()
	_m_ver.name = "Ver"
	for _wd in [
		[0, "🎬 Preview / Animación"],
		[1, "🖼 Frames definidos"],
		[2, "✏️ Frame seleccionado"],
		[3, "✂ Dividir frame"],
		[4, "🔲 Auto-detección"],
		[5, "📐 Snap al seleccionar"],
		[6, "🎬 Crear GRH Animado"],
		[7, "📝 Archivos INIT"],
		[8, "🔍 GRH Indexados"],
	]:
		_m_ver.add_check_item(_wd[1], _wd[0])
	_m_ver.add_separator()
	_m_ver.add_item("Ajustar canvas", 20)
	_m_ver.add_item("Zoom 1:1", 21)
	_m_ver.id_pressed.connect(_on_ver_menu)
	mb.add_child(_m_ver)

	return mb


func _build_toolbar() -> Control:
	var bar := PanelContainer.new()
	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 6)
	hbox.add_theme_constant_override("margin_left", 6)
	hbox.add_theme_constant_override("margin_right", 6)
	bar.add_child(hbox)

	# Cargar INIT folder o .ind individual
	var btn_load_init := _btn("📂 Cargar carpeta INIT...", _on_load_init_folder)
	btn_load_init.add_theme_color_override("font_color", Color(0.4, 1.0, 0.9))
	hbox.add_child(btn_load_init)

	var btn_load_ind := _btn("📄 Solo Graficos.ind", _on_load_ind)
	hbox.add_child(btn_load_ind)

	_lbl_max_grh = Label.new()
	_lbl_max_grh.text = "Max GrhIdx: —"
	_lbl_max_grh.add_theme_color_override("font_color", Color(0.7, 1.0, 0.7))
	hbox.add_child(_lbl_max_grh)

	hbox.add_child(VSeparator.new())

	hbox.add_child(_btn("📁 Abrir carpeta fuente...", _on_open_folder))

	hbox.add_child(VSeparator.new())

	hbox.add_child(_label("FileNum base:"))
	_spin_filenum_base = _spinbox(1, 99999, 1, func(v): _filenum_base = int(v))
	hbox.add_child(_spin_filenum_base)

	hbox.add_child(_label("Próx. GrhIdx:"))
	_spin_next_grh = _spinbox(1, 999999, 1, func(v): _next_grh_index = int(v))
	hbox.add_child(_spin_next_grh)

	hbox.add_child(VSeparator.new())

	# Cliente
	var btn_client := _btn("🗂 Carpeta cliente...", _on_set_client_folder)
	hbox.add_child(btn_client)

	# Spacer
	var sp := Control.new()
	sp.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(sp)

	# Indexar (acción principal)
	var btn_index_tb := Button.new()
	btn_index_tb.text = "✅ INDEXAR"
	btn_index_tb.add_theme_color_override("font_color", Color(0.2, 1.0, 0.4))
	btn_index_tb.add_theme_font_size_override("font_size", 13)
	btn_index_tb.pressed.connect(_on_index_image)
	hbox.add_child(btn_index_tb)

	var btn_copy_tb := _btn("📋 Copiar PNG", _on_copy_png_to_client)
	hbox.add_child(btn_copy_tb)

	hbox.add_child(VSeparator.new())

	# Guardar
	var btn_save := _btn("💾 Guardar Graficos.ind", _on_save_ind)
	btn_save.add_theme_color_override("font_color", Color(0.4, 1.0, 0.5))
	hbox.add_child(btn_save)

	return bar


func _build_left_panel() -> Control:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = 230

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	vbox.add_child(_heading("📋 Archivos en carpeta"))

	_file_list = ItemList.new()
	_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_file_list.icon_mode = ItemList.ICON_MODE_LEFT
	_file_list.fixed_icon_size = Vector2i(48, 48)
	_file_list.item_selected.connect(_on_file_selected)
	vbox.add_child(_file_list)

	# Info carpeta
	var btn_refresh := _btn("↻ Recargar lista", _on_refresh_folder)
	vbox.add_child(btn_refresh)

	return panel


func _build_center_panel() -> Control:
	var panel := PanelContainer.new()
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	panel.add_child(vbox)

	# Barra de zoom
	var zoom_bar := HBoxContainer.new()
	zoom_bar.add_theme_constant_override("separation", 4)
	vbox.add_child(zoom_bar)

	zoom_bar.add_child(_btn("🔍+", func(): _canvas.zoom_in()))
	zoom_bar.add_child(_btn("🔍-", func(): _canvas.zoom_out()))
	zoom_bar.add_child(_btn("1:1", func(): _canvas.zoom_reset()))
	zoom_bar.add_child(_btn("⊞ Ajustar", func(): _canvas.fit_to_canvas()))
	var sp := Control.new()
	sp.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	zoom_bar.add_child(sp)
	zoom_bar.add_child(_label("Clic+arrastrar → nuevo frame | Clic en frame → seleccionar | Rueda → zoom | Clic derecho → pan"))

	# Canvas
	_canvas = SpriteCanvas.new()
	_canvas.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_canvas.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_canvas.focus_mode = Control.FOCUS_CLICK
	_canvas.frame_drawn.connect(_on_canvas_frame_drawn)
	_canvas.frame_selected.connect(_on_canvas_frame_selected)
	_canvas.blob_clicked.connect(_on_canvas_blob_clicked)
	_canvas.frame_resized.connect(_on_canvas_frame_resized)
	_canvas.frame_delete_pressed.connect(func(idx): _delete_frame(idx))
	vbox.add_child(_canvas)

	return panel


func _build_tool_windows() -> void:
	_win_preview      = _make_tool_window("🎬 Preview / Animación",  _build_preview_content(),      0, Vector2i(360, 280))
	_win_frames       = _make_tool_window("🖼 Frames definidos",      _build_frames_content(),       1, Vector2i(360, 270))
	_win_frame_props  = _make_tool_window("✏️ Frame seleccionado",     _build_frame_props_content(),  2, Vector2i(360, 240))
	_win_split        = _make_tool_window("✂ Dividir frame",          _build_split_content(),        3, Vector2i(360, 220))
	_win_autodetect   = _make_tool_window("🔲 Auto-detección",         _build_autodetect_content(),   4, Vector2i(380, 460))
	_win_snap         = _make_tool_window("📐 Snap al seleccionar",    _build_snap_content(),         5, Vector2i(360, 200))
	_win_anim_creator = _make_tool_window("🎬 Crear GRH Animado",     _build_anim_creator_content(), 6, Vector2i(400, 230))
	_win_init_files   = _make_tool_window("📝 Archivos INIT",          _build_init_files_content(),   7, Vector2i(460, 460))
	_win_grh_viewer   = _make_tool_window("🔍 GRH Indexados",          _build_grh_viewer_content(),   8, Vector2i(460, 460))

	# Reposicionar ahora y cada vez que el usuario redimensione/maximize la ventana
	_reposition_tool_windows()
	get_viewport().get_window().size_changed.connect(_reposition_tool_windows)

	# Mostrar columna derecha por defecto
	_win_preview.show()
	_win_frames.show()
	_win_frame_props.show()
	_rebuild_ver_checks()


func _reposition_tool_windows() -> void:
	if _win_preview == null:
		return
	var ss  := DisplayServer.screen_get_size()
	var wp  := DisplayServer.window_get_position()
	var ww  := DisplayServer.window_get_size().x
	var wh  := DisplayServer.window_get_size().y
	# Columna derecha (ventanas por defecto)
	var rx  := clampi(wp.x + ww - 370, 0, ss.x - 200)
	# Columna central-derecha (ventanas secundarias)
	var mx  := clampi(wp.x + ww - 760, 0, ss.x - 200)
	# Y inicial: justo debajo de la barra de título del OS
	var top := clampi(wp.y + 50, 0, ss.y - 200)
	# Altura disponible para distribuir ventanas verticalmente
	var avail_h := wh - 80

	# Columna derecha: Preview + Frames + Frame Props distribuidos verticalmente
	var h_prev  := clampi(avail_h / 3, 220, 340)
	var h_frame := clampi(avail_h / 3, 200, 310)
	var h_props := avail_h - h_prev - h_frame

	_win_preview.position     = Vector2i(rx, top)
	_win_preview.size         = Vector2i(360, h_prev)
	_win_frames.position      = Vector2i(rx, top + h_prev)
	_win_frames.size          = Vector2i(360, h_frame)
	_win_frame_props.position = Vector2i(rx, top + h_prev + h_frame)
	_win_frame_props.size     = Vector2i(360, h_props)

	# Columna central-derecha: posiciones fijas, el usuario las mueve si quiere
	_win_split.position        = Vector2i(mx, top)
	_win_autodetect.position   = Vector2i(mx, top + 230)
	_win_snap.position         = Vector2i(mx, top)
	_win_anim_creator.position = Vector2i(mx, top + 210)
	_win_init_files.position   = Vector2i(mx, top)
	_win_grh_viewer.position   = Vector2i(mx, top + 470)


func _make_tool_window(title: String, content: Control, ver_id: int, init_size: Vector2i) -> Window:
	var win := Window.new()
	win.title = title
	win.min_size = Vector2i(200, 120)
	win.size = init_size
	win.exclusive = false
	win.hide()
	win.close_requested.connect(func():
		win.hide()
		_set_ver_checked(ver_id, false))
	var mg := MarginContainer.new()
	mg.set_anchors_preset(Control.PRESET_FULL_RECT)
	mg.add_theme_constant_override("margin_left", 4)
	mg.add_theme_constant_override("margin_right", 4)
	mg.add_theme_constant_override("margin_top", 4)
	mg.add_theme_constant_override("margin_bottom", 4)
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	mg.add_child(content)
	win.add_child(mg)
	add_child(win)
	return win


func _build_preview_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	_preview = FramePreviewPanel.new()
	_preview.custom_minimum_size = Vector2(0, 160)
	_preview.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_child(_preview)

	var anim_bar := HBoxContainer.new()
	anim_bar.add_theme_constant_override("separation", 3)
	vbox.add_child(anim_bar)

	var btn_prev_frame := _btn("◀", func(): _anim_step(-1))
	btn_prev_frame.custom_minimum_size.x = 26
	anim_bar.add_child(btn_prev_frame)

	_btn_play = Button.new()
	_btn_play.text = "▶ Play"
	_btn_play.toggle_mode = true
	_btn_play.toggled.connect(func(on: bool):
		_anim_playing = on
		_anim_time = 0.0
		_btn_play.text = "⏸ Pausa" if on else "▶ Play")
	anim_bar.add_child(_btn_play)

	var btn_next_frame := _btn("▶|", func(): _anim_step(1))
	btn_next_frame.custom_minimum_size.x = 26
	anim_bar.add_child(btn_next_frame)

	anim_bar.add_child(_label("FPS:"))
	_spin_anim_fps = _spinbox(1, 60, 8, func(v): _anim_fps = v)
	_spin_anim_fps.custom_minimum_size.x = 52
	anim_bar.add_child(_spin_anim_fps)

	_lbl_anim_frame = Label.new()
	_lbl_anim_frame.text = "—"
	_lbl_anim_frame.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_lbl_anim_frame.add_theme_font_size_override("font_size", 11)
	_lbl_anim_frame.add_theme_color_override("font_color", Color(0.65, 0.65, 0.65))
	anim_bar.add_child(_lbl_anim_frame)

	return vbox


func _build_frames_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)

	var scroll_frames := ScrollContainer.new()
	scroll_frames.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll_frames.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(scroll_frames)

	_frame_list_vbox = VBoxContainer.new()
	_frame_list_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_frame_list_vbox.add_theme_constant_override("separation", 2)
	scroll_frames.add_child(_frame_list_vbox)

	vbox.add_child(_btn("🗑 Limpiar todos", _on_clear_frames))
	return vbox


func _build_frame_props_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	_lbl_frame_info = Label.new()
	_lbl_frame_info.text = "(ninguno)"
	_lbl_frame_info.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
	vbox.add_child(_lbl_frame_info)

	var grid_props := GridContainer.new()
	grid_props.columns = 4
	grid_props.add_theme_constant_override("h_separation", 4)
	grid_props.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid_props)

	grid_props.add_child(_label("SX:"))
	_spin_sx = _spinbox(0, 16383, 0, func(_v): _apply_frame_props())
	grid_props.add_child(_spin_sx)
	grid_props.add_child(_label("SY:"))
	_spin_sy = _spinbox(0, 16383, 0, func(_v): _apply_frame_props())
	grid_props.add_child(_spin_sy)

	grid_props.add_child(_label("W:"))
	_spin_fw = _spinbox(1, 16383, 32, func(_v): _apply_frame_props())
	grid_props.add_child(_spin_fw)
	grid_props.add_child(_label("H:"))
	_spin_fh = _spinbox(1, 16383, 32, func(_v): _apply_frame_props())
	grid_props.add_child(_spin_fh)

	grid_props.add_child(_label("GrhIdx:"))
	_spin_grh_override = _spinbox(1, 999999, 1, func(_v): _apply_frame_props())
	grid_props.add_child(_spin_grh_override)
	grid_props.add_child(Label.new())
	grid_props.add_child(Label.new())

	var hbtn := HBoxContainer.new()
	hbtn.add_theme_constant_override("separation", 4)
	vbox.add_child(hbtn)
	hbtn.add_child(_btn("🗑 Eliminar", func(): _delete_frame(_selected_frame_idx)))
	hbtn.add_child(_btn("+ Nuevo manual", _on_add_manual_frame))

	return vbox


func _build_split_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	var split_row := HBoxContainer.new()
	split_row.add_theme_constant_override("separation", 4)
	vbox.add_child(split_row)
	split_row.add_child(_label("Celda W:"))
	_spin_split_w = _spinbox(1, 2048, 32, null)
	split_row.add_child(_spin_split_w)
	split_row.add_child(_label("H:"))
	_spin_split_h = _spinbox(1, 2048, 32, null)
	split_row.add_child(_spin_split_h)

	var hsp := HBoxContainer.new()
	hsp.add_theme_constant_override("separation", 3)
	vbox.add_child(hsp)
	hsp.add_child(_label("Presets:"))
	for sp_def in [[16,16],[32,32],[32,48],[48,48],[64,64],[128,128]]:
		var spw: int = sp_def[0]; var sph: int = sp_def[1]
		var spbtn := _btn("%d×%d" % [spw, sph], func(): _set_split_preset(spw, sph))
		spbtn.custom_minimum_size.x = 44
		hsp.add_child(spbtn)

	var lbl_info := Label.new()
	lbl_info.text = "Divide el frame activo en celdas y reemplaza con los sub-frames."
	lbl_info.add_theme_font_size_override("font_size", 10)
	lbl_info.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	lbl_info.autowrap_mode = TextServer.AUTOWRAP_WORD
	vbox.add_child(lbl_info)

	var btn_split := _btn("✂  Dividir en celdas", _on_split_frame)
	btn_split.add_theme_color_override("font_color", Color(0.6, 1.0, 0.5))
	vbox.add_child(btn_split)
	return vbox


func _build_autodetect_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	vbox.add_child(_heading("🔲 Cuadrícula"))
	var grid_detect := GridContainer.new()
	grid_detect.columns = 4
	grid_detect.add_theme_constant_override("h_separation", 4)
	grid_detect.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid_detect)

	grid_detect.add_child(_label("Celda W:"))
	_spin_cell_w = _spinbox(1, 2048, 32, null)
	grid_detect.add_child(_spin_cell_w)
	grid_detect.add_child(_label("H:"))
	_spin_cell_h = _spinbox(1, 2048, 32, null)
	grid_detect.add_child(_spin_cell_h)
	grid_detect.add_child(_label("OffX:"))
	_spin_off_x = _spinbox(0, 2048, 0, null)
	grid_detect.add_child(_spin_off_x)
	grid_detect.add_child(_label("OffY:"))
	_spin_off_y = _spinbox(0, 2048, 0, null)
	grid_detect.add_child(_spin_off_y)
	grid_detect.add_child(_label("MrgX:"))
	_spin_margin_x = _spinbox(0, 512, 0, null)
	grid_detect.add_child(_spin_margin_x)
	grid_detect.add_child(_label("MrgY:"))
	_spin_margin_y = _spinbox(0, 512, 0, null)
	grid_detect.add_child(_spin_margin_y)

	_chk_skip_empty = CheckButton.new()
	_chk_skip_empty.text = "Saltar celdas vacías"
	_chk_skip_empty.button_pressed = true
	vbox.add_child(_chk_skip_empty)

	var hpreset := HBoxContainer.new()
	hpreset.add_theme_constant_override("separation", 4)
	vbox.add_child(hpreset)
	hpreset.add_child(_label("Presets:"))
	for preset in [[32,32],[32,48],[48,48],[64,64],[96,96],[128,128]]:
		var pw: int = preset[0]; var ph: int = preset[1]
		var pbtn := _btn("%d×%d" % [pw, ph], func(): _set_grid_preset(pw, ph))
		pbtn.custom_minimum_size.x = 48
		hpreset.add_child(pbtn)

	var btn_grid := _btn("▶ Detectar grid", _on_detect_grid)
	btn_grid.add_theme_color_override("font_color", Color(0.4, 0.9, 1.0))
	vbox.add_child(btn_grid)

	vbox.add_child(HSeparator.new())
	vbox.add_child(_heading("🔵 Blobs (alfa)"))

	var grid_blob := GridContainer.new()
	grid_blob.columns = 4
	grid_blob.add_theme_constant_override("h_separation", 4)
	grid_blob.add_theme_constant_override("v_separation", 4)
	vbox.add_child(grid_blob)

	grid_blob.add_child(_label("Alpha %:"))
	_spin_alpha_thresh = _spinbox(0, 100, 3, null)
	grid_blob.add_child(_spin_alpha_thresh)
	grid_blob.add_child(_label("Min px:"))
	_spin_min_size = _spinbox(1, 512, 4, null)
	grid_blob.add_child(_spin_min_size)
	grid_blob.add_child(_label("Padding:"))
	_spin_padding = _spinbox(0, 64, 1, null)
	grid_blob.add_child(_spin_padding)
	grid_blob.add_child(Label.new()); grid_blob.add_child(Label.new())

	var btn_blob := _btn("▶ Detectar blobs", _on_detect_blobs)
	btn_blob.add_theme_color_override("font_color", Color(1.0, 0.8, 0.4))
	vbox.add_child(btn_blob)

	var lbl_warn := Label.new()
	lbl_warn.text = "⚠ Blob detection es lento en imágenes >512px"
	lbl_warn.add_theme_color_override("font_color", Color(0.9, 0.7, 0.3))
	lbl_warn.add_theme_font_size_override("font_size", 11)
	lbl_warn.autowrap_mode = TextServer.AUTOWRAP_WORD
	vbox.add_child(lbl_warn)
	return vbox


func _build_snap_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	var snap_mode_row := HBoxContainer.new()
	snap_mode_row.add_theme_constant_override("separation", 3)
	vbox.add_child(snap_mode_row)
	for snap_mode_def in [["Sin snap", 0], ["Pot.2", 2], ["Cuad.P2", 3], ["Múltiplo", 1]]:
		var mlbl: String = snap_mode_def[0]
		var mval: int    = snap_mode_def[1]
		var mbtn := _btn(mlbl, func(): _set_snap_mode(mval))
		mbtn.custom_minimum_size.x = 54
		snap_mode_row.add_child(mbtn)

	_snap_multiple_row = HBoxContainer.new()
	_snap_multiple_row.add_theme_constant_override("separation", 4)
	_snap_multiple_row.visible = false
	vbox.add_child(_snap_multiple_row)
	_snap_multiple_row.add_child(_label("X:"))
	_spin_snap_x = _spinbox(1, 512, 32, func(_v): _on_snap_changed())
	_spin_snap_x.custom_minimum_size.x = 60
	_snap_multiple_row.add_child(_spin_snap_x)
	_snap_multiple_row.add_child(_label("Y:"))
	_spin_snap_y = _spinbox(1, 512, 32, func(_v): _on_snap_changed())
	_spin_snap_y.custom_minimum_size.x = 60
	_snap_multiple_row.add_child(_spin_snap_y)

	var hsnap_pre := HBoxContainer.new()
	hsnap_pre.add_theme_constant_override("separation", 3)
	vbox.add_child(hsnap_pre)
	hsnap_pre.add_child(_label("Presets:"))
	for sp in [["32×32", 32, 32], ["32×48", 32, 48], ["64×64", 64, 64], ["96×96", 96, 96]]:
		var slbl: String = sp[0]; var sx: int = sp[1]; var sy: int = sp[2]
		var sbtn := _btn(slbl, func(): _set_snap_multiple_preset(sx, sy))
		sbtn.custom_minimum_size.x = 48
		hsnap_pre.add_child(sbtn)
	return vbox


func _build_anim_creator_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)

	var lbl_hint := Label.new()
	lbl_hint.text = "GrhIndex de frames estáticos separados por comas.\nO usa el botón para cargar los frames actuales."
	lbl_hint.add_theme_font_size_override("font_size", 10)
	lbl_hint.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	lbl_hint.autowrap_mode = TextServer.AUTOWRAP_WORD
	vbox.add_child(lbl_hint)

	_edit_anim_indices = LineEdit.new()
	_edit_anim_indices.placeholder_text = "Ej: 1001, 1002, 1003, 1004"
	_edit_anim_indices.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_child(_edit_anim_indices)

	var anim_ctrl_row := HBoxContainer.new()
	anim_ctrl_row.add_theme_constant_override("separation", 4)
	vbox.add_child(anim_ctrl_row)

	anim_ctrl_row.add_child(_label("Speed:"))
	_spin_anim_speed = _spinbox(0.001, 999.0, 8.0, null)
	_spin_anim_speed.step = 0.1
	_spin_anim_speed.custom_minimum_size.x = 70
	anim_ctrl_row.add_child(_spin_anim_speed)

	var btn_use := _btn("← Usar frames actuales", func():
		var indices: PackedStringArray = PackedStringArray()
		for f in _current_frames:
			indices.append(str(f.grh_index))
		_edit_anim_indices.text = ", ".join(indices))
	anim_ctrl_row.add_child(btn_use)

	var btn_create := _btn("✅ Crear GRH Animado", _on_create_anim_grh)
	btn_create.add_theme_color_override("font_color", Color(0.4, 1.0, 0.8))
	vbox.add_child(btn_create)
	return vbox


func _build_init_files_content() -> Control:
	var vsplit := VSplitContainer.new()
	vsplit.split_offset = 160

	var top_vbox := VBoxContainer.new()
	top_vbox.add_theme_constant_override("separation", 4)
	vsplit.add_child(top_vbox)

	_init_file_list = ItemList.new()
	_init_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_file_list.item_selected.connect(_on_init_file_selected)
	top_vbox.add_child(_init_file_list)

	var init_btn_row := HBoxContainer.new()
	init_btn_row.add_theme_constant_override("separation", 4)
	top_vbox.add_child(init_btn_row)

	var btn_save_init := _btn("💾 Guardar archivo", _save_init_file)
	btn_save_init.add_theme_color_override("font_color", Color(0.4, 1.0, 0.5))
	init_btn_row.add_child(btn_save_init)

	init_btn_row.add_child(_btn("↺ Recargar", func():
		if not _init_current_file_path.is_empty():
			_load_init_file_to_editor(_init_current_file_path)))

	_init_text_edit = TextEdit.new()
	_init_text_edit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_init_text_edit.placeholder_text = "Selecciona un archivo INIT para ver su contenido."
	vsplit.add_child(_init_text_edit)

	return vsplit


func _build_grh_viewer_content() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)

	_lbl_grh_count = Label.new()
	_lbl_grh_count.text = "Selecciona una imagen del cliente"
	_lbl_grh_count.add_theme_font_size_override("font_size", 11)
	_lbl_grh_count.add_theme_color_override("font_color", Color(0.65, 0.65, 0.65))
	vbox.add_child(_lbl_grh_count)

	var grh_scroll := ScrollContainer.new()
	grh_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	grh_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	vbox.add_child(grh_scroll)

	_grh_viewer_vbox = VBoxContainer.new()
	_grh_viewer_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_grh_viewer_vbox.add_theme_constant_override("separation", 2)
	grh_scroll.add_child(_grh_viewer_vbox)

	return vbox


# ── Window toggles (Ver menu) ──────────────────────────────────────────────────

func _get_win_for_ver_id(id: int) -> Window:
	match id:
		0: return _win_preview
		1: return _win_frames
		2: return _win_frame_props
		3: return _win_split
		4: return _win_autodetect
		5: return _win_snap
		6: return _win_anim_creator
		7: return _win_init_files
		8: return _win_grh_viewer
	return null


func _toggle_window(ver_id: int) -> void:
	var win := _get_win_for_ver_id(ver_id)
	if win == null:
		return
	if win.visible:
		win.hide()
		_set_ver_checked(ver_id, false)
	else:
		win.show()
		_set_ver_checked(ver_id, true)


func _set_ver_checked(ver_id: int, checked: bool) -> void:
	if _m_ver == null:
		return
	var idx := _m_ver.get_item_index(ver_id)
	if idx >= 0:
		_m_ver.set_item_checked(idx, checked)


func _rebuild_ver_checks() -> void:
	if _m_ver == null:
		return
	var wins := [_win_preview, _win_frames, _win_frame_props, _win_split,
				 _win_autodetect, _win_snap, _win_anim_creator, _win_init_files, _win_grh_viewer]
	for i in range(wins.size()):
		var idx := _m_ver.get_item_index(i)
		if idx >= 0 and wins[i] != null:
			_m_ver.set_item_checked(idx, wins[i].visible)


func _on_ver_menu(id: int) -> void:
	if id < 20:
		_toggle_window(id)
	elif id == 20:
		_canvas.fit_to_canvas()
	elif id == 21:
		_canvas.zoom_reset()



func _build_statusbar() -> Control:
	var bar := PanelContainer.new()
	_lbl_status = Label.new()
	_lbl_status.text = ""
	_lbl_status.add_theme_font_size_override("font_size", 11)
	bar.add_child(_lbl_status)
	return bar


func _build_dialogs() -> void:
	# Diálogo para abrir .ind
	_file_dialog_ind = FileDialog.new()
	_file_dialog_ind.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_ind.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	_file_dialog_ind.filters = ["*.ind;Graficos.ind", "*;Todos los archivos"]
	_file_dialog_ind.file_selected.connect(_load_ind_from_path)
	add_child(_file_dialog_ind)

	# Diálogo para guardar .ind
	_file_dialog_save = FileDialog.new()
	_file_dialog_save.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_save.file_mode = FileDialog.FILE_MODE_SAVE_FILE
	_file_dialog_save.filters = ["*.ind;Graficos.ind"]
	_file_dialog_save.current_file = "Graficos.ind"
	_file_dialog_save.file_selected.connect(_save_ind_to_path)
	add_child(_file_dialog_save)

	# Diálogo para abrir carpeta fuente
	_file_dialog_folder = FileDialog.new()
	_file_dialog_folder.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_folder.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	_file_dialog_folder.dir_selected.connect(_load_folder)
	add_child(_file_dialog_folder)

	# Diálogo para carpeta cliente
	_file_dialog_client = FileDialog.new()
	_file_dialog_client.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_client.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	_file_dialog_client.dir_selected.connect(func(d): _client_graficos_path = d; _update_status("Carpeta cliente: " + d))
	add_child(_file_dialog_client)

	# Diálogo para carpeta INIT completa
	_file_dialog_init = FileDialog.new()
	_file_dialog_init.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_init.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	_file_dialog_init.dir_selected.connect(_load_init_folder)
	add_child(_file_dialog_init)

	# Diálogo para abrir carpeta CLIENT completa
	_file_dialog_client_folder = FileDialog.new()
	_file_dialog_client_folder.access = FileDialog.ACCESS_FILESYSTEM
	_file_dialog_client_folder.file_mode = FileDialog.FILE_MODE_OPEN_DIR
	_file_dialog_client_folder.dir_selected.connect(_load_client_folder)
	add_child(_file_dialog_client_folder)


# ── Handlers de toolbar ───────────────────────────────────────────────────────

func _on_load_init_folder() -> void:
	_file_dialog_init.popup_centered_ratio(0.7)


func _load_init_folder(path: String) -> void:
	_init_folder = path
	var msgs: Array[String] = []

	# Graficos.ind — obligatorio
	var grh_path := path.path_join("Graficos.ind")
	if FileAccess.file_exists(grh_path):
		_grh_data = GrhIO.load_ind(grh_path)
		_ind_path = grh_path
		_next_grh_index = _grh_data["max_index"] + 1
		_spin_next_grh.value = _next_grh_index
		_lbl_max_grh.text = "Max GrhIdx: %d  (%d entradas)" % [
			_grh_data["max_index"], _grh_data["entries"].size()]
		msgs.append("Graficos.ind: %d GRHs" % _grh_data["entries"].size())
	else:
		msgs.append("⚠ Graficos.ind no encontrado")

	# Personajes.ind — optional
	var body_path := path.path_join("Personajes.ind")
	if FileAccess.file_exists(body_path):
		_bodies_data = _load_personajes_ind(body_path)
		msgs.append("Personajes.ind: %d cuerpos" % _bodies_data.size())

	# Fxs.ind — optional
	var fxs_path := path.path_join("Fxs.ind")
	if FileAccess.file_exists(fxs_path):
		_load_fxs_ind(fxs_path)
		msgs.append("Fxs.ind: %d efectos" % _fxs_data.size())

	# Otros .ind — solo listar
	var dir := DirAccess.open(path)
	if dir:
		dir.list_dir_begin()
		var f := dir.get_next()
		while f != "":
			if f.get_extension().to_lower() == "ind" and \
			   f.to_lower() != "graficos.ind" and f.to_lower() != "personajes.ind":
				msgs.append("%s ✓" % f)
			f = dir.get_next()
		dir.list_dir_end()

	_update_status("📂 INIT cargado: " + " | ".join(msgs))


func _load_personajes_ind(path: String) -> Array:
	var bodies: Array = []
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return bodies
	# Saltar MiCabecera (263 bytes)
	if f.get_length() < 265:
		f.close()
		return bodies
	f.seek(263)
	var count: int = f.get_16()
	for i in range(1, count + 1):
		var body := {
			"index": i,
			"walk_n": f.get_16(),
			"walk_e": f.get_16(),
			"walk_s": f.get_16(),
			"walk_w": f.get_16(),
			"head_x": f.get_16(),
			"head_y": f.get_16()
		}
		bodies.append(body)
	f.close()
	return bodies


func _on_load_ind() -> void:
	_file_dialog_ind.popup_centered_ratio(0.7)


func _on_save_ind() -> void:
	if _ind_path.is_empty():
		_file_dialog_save.popup_centered_ratio(0.7)
	else:
		_save_ind_to_path(_ind_path)


func _on_open_folder() -> void:
	_file_dialog_folder.popup_centered_ratio(0.7)


func _on_set_client_folder() -> void:
	_file_dialog_client.popup_centered_ratio(0.7)


func _on_refresh_folder() -> void:
	if not _source_folder.is_empty():
		_load_folder(_source_folder)


# ── Cargar Graficos.ind ───────────────────────────────────────────────────────

func _load_ind_from_path(path: String) -> void:
	_grh_data = GrhIO.load_ind(path)
	_ind_path = path
	_next_grh_index = _grh_data["max_index"] + 1
	_spin_next_grh.value = _next_grh_index
	_lbl_max_grh.text = "Max GrhIdx: %d  (%d entradas)" % [
		_grh_data["max_index"], _grh_data["entries"].size()]
	_update_status("✅ Graficos.ind cargado: %d entradas | Próximo GrhIdx: %d" % [
		_grh_data["entries"].size(), _next_grh_index])


func _save_ind_to_path(path: String) -> void:
	_ind_path = path
	if GrhIO.save_ind(path, _grh_data):
		_update_status("✅ Guardado: %s  (%d entradas, max_index=%d)" % [
			path, _grh_data["entries"].size(), _grh_data["max_index"]])
	else:
		_update_status("❌ Error al guardar: " + path)


# ── Carpeta fuente ────────────────────────────────────────────────────────────

func _load_folder(path: String) -> void:
	_source_folder = path
	_image_files.clear()
	_file_list.clear()

	var dir := DirAccess.open(path)
	if dir == null:
		_update_status("❌ No se pudo abrir la carpeta: " + path)
		return

	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		var ext := fname.get_extension().to_lower()
		if ext in ["png", "jpg", "jpeg", "bmp", "tga", "webp"]:
			_image_files.append(path.path_join(fname))
		fname = dir.get_next()
	dir.list_dir_end()

	# Orden numérico ascendente (302 antes que 3000)
	_image_files.sort_custom(func(a, b):
		return _file_sort_num(a) < _file_sort_num(b))

	_file_list.clear()
	for fp in _image_files:
		_file_list.add_item(fp.get_file())

	# Encolar carga lazy de miniaturas
	_thumb_queue.clear()
	for i in range(_image_files.size()):
		_thumb_queue.append(i)

	_update_status("📁 Carpeta cargada: %d imágenes en %s" % [_image_files.size(), path])


# ── Selección de imagen ───────────────────────────────────────────────────────

func _on_file_selected(idx: int) -> void:
	if idx < 0 or idx >= _image_files.size():
		return
	_current_image_path = _image_files[idx]
	_current_frames = []
	_selected_frame_idx = -1
	_refresh_frame_list()

	var img := _load_image_from_os_path(_current_image_path)
	if img == null:
		_update_status("❌ No se pudo cargar: " + _current_image_path)
		return

	_current_image = img
	_current_texture = ImageTexture.create_from_image(img)
	_canvas.load_image(img)
	_anim_frame_idx = 0
	_anim_playing = false
	if _btn_play != null:
		_btn_play.button_pressed = false
		_btn_play.text = "▶ Play"
	_preview.set_image(img)
	_preview.set_frames([])

	# Determinar FileNum
	if _using_client:
		var basename := _current_image_path.get_file().get_basename()
		_current_file_num = basename.to_int() if basename.is_valid_int() else 0
	else:
		_current_file_num = _get_file_num_for(_current_image_path)

	# Cargar GRHs existentes para esta imagen
	if _grh_data["entries"].size() > 0 and _current_file_num > 0:
		var grh_entries := _get_grh_entries_for_file_num(_current_file_num)
		for e in grh_entries:
			_current_frames.append({
				"sx": e.get("sx", 0), "sy": e.get("sy", 0),
				"w":  e.get("width", 32), "h": e.get("height", 32),
				"grh_index": e.grh_index,
				"file_num":  _current_file_num
			})
		_refresh_frame_list()
		_canvas.set_selected(-1)

	_refresh_grh_viewer()

	_update_status("🖼 %s — FileNum=%d — %d×%d px  (analizando blobs...)" % [
		_current_image_path.get_file(), _current_file_num, img.get_width(), img.get_height()])

	# Pre-computar mapa de blobs para hover instantáneo
	var blob_data := FrameDetector.detect_blobs_indexed(img, 0.03, 3, 1)
	_canvas.set_blob_data(
		blob_data["map"],
		blob_data["rects"],
		blob_data["id_to_rect"],
		blob_data["width"]
	)
	_update_status("🖼 %s — FileNum=%d — %d GRHs indexados — %d blobs detectados" % [
		_current_image_path.get_file(), _current_file_num,
		_current_frames.size(), blob_data["rects"].size()])


# Carga una imagen desde una ruta absoluta del OS (no res://)
# usando FileAccess + buffer para evitar la limitación del editor de Godot.
func _load_image_from_os_path(path: String) -> Image:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		_update_status("❌ No se puede leer: %s (err=%d)" % [path, FileAccess.get_open_error()])
		return null
	var bytes := f.get_buffer(f.get_length())
	f.close()

	if bytes.is_empty():
		_update_status("❌ Archivo vacío: %s" % path)
		return null

	var img := Image.new()
	var ext := path.get_extension().to_lower()
	var err: Error

	if ext == "png":
		# Intento 1: PNG tal cual
		err = img.load_png_from_buffer(bytes)
		if err != OK:
			# Intento 2: stripear chunks de metadatos problemáticos (iCCP, sRGB, gAMA, cHRM)
			var clean := _strip_png_metadata(bytes)
			err = img.load_png_from_buffer(clean)
		if err != OK:
			# Intento 3: escribir a user:// y cargar desde ahí
			var tmp := "user://tmp_load.png"
			var tf := FileAccess.open(tmp, FileAccess.WRITE)
			if tf != null:
				tf.store_buffer(bytes)
				tf.close()
				img = Image.load_from_file(tmp)
				if img != null:
					return img
			_update_status("❌ PNG no decodificable (err=%d): %s" % [err, path.get_file()])
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
		_update_status("❌ Error decodificando (err=%d): %s" % [err, path.get_file()])
		return null
	return img


# Elimina chunks de metadatos que Godot/libpng rechaza (iCCP, sRGB, gAMA, cHRM, tEXt, iTXt, zTXt)
func _strip_png_metadata(bytes: PackedByteArray) -> PackedByteArray:
	# Verificar firma PNG
	if bytes.size() < 8:
		return bytes
	var sig := [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
	for i in range(8):
		if bytes[i] != sig[i]:
			return bytes  # no es PNG válido, devolver sin cambios

	var skip_types := ["iCCP", "sRGB", "gAMA", "cHRM", "tEXt", "iTXt", "zTXt"]
	var result := PackedByteArray()
	# Copiar firma
	result.append_array(bytes.slice(0, 8))

	var pos := 8
	while pos + 12 <= bytes.size():
		var length: int = (bytes[pos] << 24) | (bytes[pos+1] << 16) | (bytes[pos+2] << 8) | bytes[pos+3]
		var chunk_end := pos + 12 + length
		if chunk_end > bytes.size():
			break
		# Leer tipo como string ASCII de 4 bytes
		var t := ""
		for i in range(4):
			t += char(bytes[pos + 4 + i])

		if t not in skip_types:
			result.append_array(bytes.slice(pos, chunk_end))

		pos = chunk_end
		if t == "IEND":
			break

	return result


func _file_sort_num(path: String) -> int:
	var name := path.get_file().get_basename()
	# Quitar prefijos de letras/guiones para obtener el número (ej: "gfx_042" → 42, "302" → 302)
	var digits := name.lstrip("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_- ")
	return digits.to_int() if digits.is_valid_int() else -1


func _load_thumb_for(idx: int) -> void:
	if idx >= _image_files.size() or idx >= _file_list.item_count:
		return
	# Carga rápida: solo PNG con fallback de strip de metadatos
	var bytes := FileAccess.get_file_as_bytes(_image_files[idx])
	if bytes.is_empty():
		return
	var img := Image.new()
	if img.load_png_from_buffer(bytes) != OK:
		var clean := _strip_png_metadata(bytes)
		if img.load_png_from_buffer(clean) != OK:
			return
	# Escalar a miniatura 48×48 manteniendo proporción
	var scale := minf(48.0 / img.get_width(), 48.0 / img.get_height())
	var tw := maxi(1, int(img.get_width()  * scale))
	var th := maxi(1, int(img.get_height() * scale))
	img.resize(tw, th, Image.INTERPOLATE_BILINEAR)
	_file_list.set_item_icon(idx, ImageTexture.create_from_image(img))


func _get_file_num_for(path: String) -> int:
	var name := path.get_file().get_basename()
	# Intentar parsear: gfx_042 → 42, o 042 → 42, etc.
	var digits := name.lstrip("abcdefghijklmnopqrstuvwxyz_ABCDEFGHIJKLMNOPQRSTUVWXYZ")
	if digits.is_valid_int():
		return _filenum_base + digits.to_int()
	# Fallback: índice en la lista
	return _filenum_base + _image_files.find(path)


# ── Canvas: nuevo frame dibujado por el usuario ───────────────────────────────

func _on_canvas_frame_drawn(rect: Rect2) -> void:
	var file_num := _get_file_num_for(_current_image_path)
	var grh_idx := _next_grh_index
	_next_grh_index += 1
	_spin_next_grh.value = _next_grh_index

	var frame := {
		"sx": int(rect.position.x),
		"sy": int(rect.position.y),
		"w": int(rect.size.x),
		"h": int(rect.size.y),
		"grh_index": grh_idx,
		"file_num": file_num
	}
	_current_frames.append(frame)
	_selected_frame_idx = _current_frames.size() - 1
	_anim_frame_idx = _selected_frame_idx
	_refresh_frame_list()
	_canvas.set_selected(_selected_frame_idx)
	_preview.show_frame(_selected_frame_idx)
	_update_props_panel()
	_update_anim_label()
	_update_status("Frame #%d dibujado: G%d  (%d,%d)  %d×%d" % [
		_selected_frame_idx, grh_idx, frame.sx, frame.sy, frame.w, frame.h])


func _on_canvas_blob_clicked(rect: Rect2i) -> void:
	_on_canvas_frame_drawn(Rect2(rect.position, rect.size))


func _on_canvas_frame_resized(index: int, new_rect: Rect2) -> void:
	if index < 0 or index >= _current_frames.size():
		return
	var f: Dictionary = _current_frames[index]
	f["sx"] = int(new_rect.position.x)
	f["sy"] = int(new_rect.position.y)
	f["w"]  = int(new_rect.size.x)
	f["h"]  = int(new_rect.size.y)
	_current_frames[index] = f
	_refresh_frame_list()
	if index == _selected_frame_idx:
		_update_props_panel()
		_preview.show_frame(index)
	_update_status("Frame G%d redimensionado: (%d,%d) %d×%d" % [
		f.grh_index, f.sx, f.sy, f.w, f.h])


func _on_canvas_frame_selected(frame_idx: int) -> void:
	_selected_frame_idx = frame_idx  # -1 = deselect
	if frame_idx >= 0:
		_anim_frame_idx = frame_idx
		_preview.show_frame(frame_idx)
	_refresh_frame_list()
	_update_props_panel()
	_update_anim_label()


# ── Lista de frames ───────────────────────────────────────────────────────────

func _refresh_frame_list() -> void:
	# Liberar filas anteriores de forma inmediata
	for child in _frame_list_vbox.get_children():
		child.free()

	for i in range(_current_frames.size()):
		var f: Dictionary = _current_frames[i]
		var is_sel: bool = (i == _selected_frame_idx)

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 2)

		var lbl := Label.new()
		lbl.text = "G%d  (%d,%d)  %d×%d" % [f.grh_index, f.sx, f.sy, f.w, f.h]
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		lbl.add_theme_font_size_override("font_size", 11)
		lbl.add_theme_color_override("font_color",
			Color(1.0, 1.0, 0.35) if is_sel else Color(0.82, 0.82, 0.82))
		row.add_child(lbl)

		var idx := i  # captura por valor para closures
		var btn_sel := Button.new()
		btn_sel.text = "✏"
		btn_sel.custom_minimum_size = Vector2(22, 18)
		btn_sel.add_theme_font_size_override("font_size", 10)
		btn_sel.pressed.connect(func(): _on_frame_row_selected(idx))
		row.add_child(btn_sel)

		var btn_del := Button.new()
		btn_del.text = "✕"
		btn_del.custom_minimum_size = Vector2(22, 18)
		btn_del.add_theme_font_size_override("font_size", 10)
		btn_del.add_theme_color_override("font_color", Color(1.0, 0.4, 0.4))
		btn_del.pressed.connect(func(): _delete_frame(idx))
		row.add_child(btn_del)

		_frame_list_vbox.add_child(row)

	_canvas.set_frames(_current_frames)
	if _preview != null:
		_preview.set_frames(_current_frames)


func _on_frame_row_selected(idx: int) -> void:
	_selected_frame_idx = idx
	_anim_frame_idx = idx
	_anim_playing = false
	if _btn_play != null:
		_btn_play.button_pressed = false
		_btn_play.text = "▶ Play"
	_preview.show_frame(idx)
	_canvas.set_selected(idx)
	_refresh_frame_list()
	_update_props_panel()
	_update_anim_label()


func _on_clear_frames() -> void:
	_current_frames = []
	_selected_frame_idx = -1
	_anim_frame_idx = 0
	_anim_playing = false
	if _btn_play != null:
		_btn_play.button_pressed = false
		_btn_play.text = "▶ Play"
	_refresh_frame_list()
	_canvas.set_selected(-1)
	_update_props_panel()
	_update_anim_label()
	_update_status("Frames limpiados.")


func _delete_frame(idx: int) -> void:
	if idx < 0 or idx >= _current_frames.size():
		return
	_current_frames.remove_at(idx)
	_selected_frame_idx = mini(_selected_frame_idx, _current_frames.size() - 1)
	_anim_frame_idx = clampi(_anim_frame_idx, 0, maxi(0, _current_frames.size() - 1))
	_refresh_frame_list()
	_canvas.set_selected(_selected_frame_idx)
	_update_props_panel()
	_update_anim_label()
	_update_status("Frame eliminado. Quedan %d." % _current_frames.size())


func _on_add_manual_frame() -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return
	var file_num := _get_file_num_for(_current_image_path)
	var frame := {
		"sx": 0, "sy": 0, "w": 32, "h": 32,
		"grh_index": _next_grh_index,
		"file_num": file_num
	}
	_current_frames.append(frame)
	_next_grh_index += 1
	_spin_next_grh.value = _next_grh_index
	_selected_frame_idx = _current_frames.size() - 1
	_anim_frame_idx = _selected_frame_idx
	_refresh_frame_list()
	_canvas.set_selected(_selected_frame_idx)
	_preview.show_frame(_selected_frame_idx)
	_update_props_panel()
	_update_anim_label()


# ── Propiedades del frame ─────────────────────────────────────────────────────

func _update_props_panel() -> void:
	if _selected_frame_idx < 0 or _selected_frame_idx >= _current_frames.size():
		_lbl_frame_info.text = "(ninguno seleccionado)"
		return
	var f: Dictionary = _current_frames[_selected_frame_idx]
	_lbl_frame_info.text = "Frame %d de %d" % [_selected_frame_idx + 1, _current_frames.size()]
	# Actualizar spinboxes sin disparar callbacks recursivos
	_spin_sx.set_value_no_signal(f.sx)
	_spin_sy.set_value_no_signal(f.sy)
	_spin_fw.set_value_no_signal(f.w)
	_spin_fh.set_value_no_signal(f.h)
	_spin_grh_override.set_value_no_signal(f.grh_index)


func _apply_frame_props() -> void:
	if _selected_frame_idx < 0 or _selected_frame_idx >= _current_frames.size():
		return
	var f: Dictionary = _current_frames[_selected_frame_idx]
	f["sx"] = int(_spin_sx.value)
	f["sy"] = int(_spin_sy.value)
	f["w"]  = int(_spin_fw.value)
	f["h"]  = int(_spin_fh.value)
	f["grh_index"] = int(_spin_grh_override.value)
	_current_frames[_selected_frame_idx] = f
	_refresh_frame_list()


# ── Dividir frame ────────────────────────────────────────────────────────────

func _set_split_preset(w: int, h: int) -> void:
	_spin_split_w.value = w
	_spin_split_h.value = h


func _on_split_frame() -> void:
	if _selected_frame_idx < 0 or _selected_frame_idx >= _current_frames.size():
		_update_status("Selecciona un frame primero para dividirlo.")
		return

	var sw: int = int(_spin_split_w.value)
	var sh: int = int(_spin_split_h.value)
	var f: Dictionary = _current_frames[_selected_frame_idx]

	var cols: int = f.w / sw
	var rows: int = f.h / sh

	if cols <= 0 or rows <= 0:
		_update_status("⚠ El frame (%d×%d) es más pequeño que la celda (%d×%d)." % [f.w, f.h, sw, sh])
		return
	if cols == 1 and rows == 1:
		_update_status("⚠ Solo cabe 1 celda en este frame — no hay nada que dividir.")
		return

	var file_num: int = f.file_num
	var base_x: int = f.sx
	var base_y: int = f.sy
	var insert_at: int = _selected_frame_idx

	# Eliminar el frame original
	_current_frames.remove_at(_selected_frame_idx)

	# Insertar sub-frames en su lugar (izq→der, arriba→abajo)
	for row in range(rows):
		for col in range(cols):
			var sub := {
				"sx": base_x + col * sw,
				"sy": base_y + row * sh,
				"w":  sw,
				"h":  sh,
				"grh_index": _next_grh_index,
				"file_num":  file_num
			}
			_current_frames.insert(insert_at + row * cols + col, sub)
			_next_grh_index += 1

	_spin_next_grh.value = _next_grh_index
	_selected_frame_idx = insert_at
	_anim_frame_idx = insert_at
	_refresh_frame_list()
	_canvas.set_selected(_selected_frame_idx)
	_preview.show_frame(_selected_frame_idx)
	_update_props_panel()
	_update_anim_label()
	_update_status("✂ Dividido en %d×%d = %d celdas de %d×%d px" % [
		cols, rows, cols * rows, sw, sh])


# ── Auto-detección ────────────────────────────────────────────────────────────

func _set_grid_preset(w: int, h: int) -> void:
	_spin_cell_w.value = w
	_spin_cell_h.value = h


func _on_detect_grid() -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return

	var detected := FrameDetector.detect_grid(
		_current_image,
		int(_spin_cell_w.value), int(_spin_cell_h.value),
		int(_spin_off_x.value),  int(_spin_off_y.value),
		int(_spin_margin_x.value), int(_spin_margin_y.value),
		_chk_skip_empty.button_pressed
	)

	_apply_detected_frames(detected)
	_update_status("Grid: %d frames detectados (%d×%d px)" % [
		detected.size(), int(_spin_cell_w.value), int(_spin_cell_h.value)])


func _on_detect_blobs() -> void:
	if _current_image == null:
		_update_status("Carga una imagen primero.")
		return

	if _current_image.get_width() > 1024 or _current_image.get_height() > 1024:
		_update_status("⚠ Imagen grande — blob detection puede tardar varios segundos...")

	var alpha_thresh := float(_spin_alpha_thresh.value) / 100.0
	var blob_data := FrameDetector.detect_blobs_indexed(
		_current_image,
		alpha_thresh,
		int(_spin_min_size.value),
		int(_spin_padding.value)
	)
	var detected: Array = blob_data["rects"]

	_apply_detected_frames(detected)
	_update_status("Blobs: %d regiones detectadas" % detected.size())


func _apply_detected_frames(detected: Array) -> void:
	if detected.is_empty():
		_update_status("No se detectaron frames. Ajusta los parámetros.")
		return

	var file_num := _get_file_num_for(_current_image_path)

	# Preguntar si limpiar frames existentes
	# (en Godot no hay MessageBox fácil en runtime, simplificamos: siempre limpia)
	_current_frames = []

	for det in detected:
		var sx: int; var sy: int; var sw: int; var sh: int
		if det is Rect2i:
			sx = det.position.x; sy = det.position.y; sw = det.size.x; sh = det.size.y
		elif det is Dictionary:
			sx = det.get("sx", 0); sy = det.get("sy", 0)
			sw = det.get("w", 32);  sh = det.get("h", 32)
		else:
			continue
		var frame := {
			"sx": sx, "sy": sy, "w": sw, "h": sh,
			"grh_index": _next_grh_index,
			"file_num": file_num
		}
		_current_frames.append(frame)
		_next_grh_index += 1

	_spin_next_grh.value = _next_grh_index
	_selected_frame_idx = -1
	_anim_frame_idx = 0
	_refresh_frame_list()
	_canvas.set_selected(-1)
	_update_props_panel()
	_update_anim_label()


# ── Indexar imagen ────────────────────────────────────────────────────────────

func _on_index_image() -> void:
	if _current_frames.is_empty():
		_update_status("⚠ No hay frames definidos para esta imagen.")
		return

	var added := 0
	for frame in _current_frames:
		var entry := {
			"grh_index": frame.grh_index,
			"num_frames": 1,
			"file_num": frame.file_num,
			"sx": frame.sx,
			"sy": frame.sy,
			"width": frame.w,
			"height": frame.h
		}
		_grh_data["entries"][frame.grh_index] = entry
		if frame.grh_index > _grh_data["max_index"]:
			_grh_data["max_index"] = frame.grh_index
		added += 1

	_lbl_max_grh.text = "Max GrhIdx: %d  (%d entradas)" % [
		_grh_data["max_index"], _grh_data["entries"].size()]

	_update_status("✅ Indexados %d frames. Total en .ind: %d entradas. FileNum=%d" % [
		added, _grh_data["entries"].size(), _current_frames[0].file_num if added > 0 else 0])


# ── Copiar PNG al cliente ─────────────────────────────────────────────────────

func _on_copy_png_to_client() -> void:
	if _current_image_path.is_empty():
		_update_status("Selecciona una imagen primero.")
		return
	if _client_graficos_path.is_empty():
		_update_status("Configura la carpeta cliente primero (botón 'Carpeta cliente...').")
		_file_dialog_client.popup_centered_ratio(0.7)
		return

	var file_num := _get_file_num_for(_current_image_path)
	var dest := _client_graficos_path.path_join("%d.png" % file_num)

	var src_bytes := FileAccess.get_file_as_bytes(_current_image_path)
	if src_bytes.is_empty():
		_update_status("❌ No se pudo leer el PNG fuente.")
		return

	var f := FileAccess.open(dest, FileAccess.WRITE)
	if f == null:
		_update_status("❌ No se pudo escribir en: " + dest)
		return
	f.store_buffer(src_bytes)
	f.close()

	_update_status("✅ Copiado: %s → %s" % [_current_image_path.get_file(), dest])


# ── Animación ────────────────────────────────────────────────────────────────

func _anim_step(dir: int) -> void:
	if _current_frames.is_empty():
		return
	_anim_frame_idx = (_anim_frame_idx + dir + _current_frames.size()) % _current_frames.size()
	_preview.show_frame(_anim_frame_idx)
	_update_anim_label()


func _update_anim_label() -> void:
	if _lbl_anim_frame == null:
		return
	if _current_frames.is_empty():
		_lbl_anim_frame.text = "Sin frames"
	else:
		_lbl_anim_frame.text = "%d / %d" % [_anim_frame_idx + 1, _current_frames.size()]


# ── Snap ─────────────────────────────────────────────────────────────────────

func _set_snap_mode(mode: int) -> void:
	_snap_mode = mode
	if _snap_multiple_row != null:
		_snap_multiple_row.visible = (mode == 1)
	_on_snap_changed()
	var names := ["Sin snap", "Múltiplo", "Pot. de 2 por dimensión", "Cuadrado Pot. de 2"]
	_update_status("Snap: %s" % names[clampi(mode, 0, 3)])


func _set_snap_multiple_preset(sx: int, sy: int) -> void:
	_snap_mode = 1
	if _snap_multiple_row != null:
		_snap_multiple_row.visible = true
	_spin_snap_x.value = sx
	_spin_snap_y.value = sy
	_on_snap_changed()


func _on_snap_changed() -> void:
	var sx: int = int(_spin_snap_x.value) if _spin_snap_x != null else 32
	var sy: int = int(_spin_snap_y.value) if _spin_snap_y != null else 32
	_canvas.set_snap(_snap_mode, sx, sy)


# ── Abrir carpeta Cliente ─────────────────────────────────────────────────────

func _on_archivo_menu(id: int) -> void:
	if id >= 200:
		var idx := id - 200
		if idx < _recent_clients.size():
			_load_client_folder(_recent_clients[idx])
		return
	match id:
		0: _file_dialog_client_folder.popup_centered_ratio(0.7)
		1: _file_dialog_folder.popup_centered_ratio(0.7)
		99: get_tree().quit()


func _on_guardar_menu(id: int) -> void:
	match id:
		0:  # Graficos.ind
			if _ind_path.is_empty():
				_file_dialog_save.popup_centered_ratio(0.7)
			else:
				_save_ind_to_path(_ind_path)
		1:  # Personajes.ind
			_save_personajes_ind()
		2:  # Fxs.ind
			_save_fxs_ind()
		10: # Archivo INIT activo
			_save_init_file()


func _load_client_folder(path: String) -> void:
	_using_client = true
	_client_graficos_path = path
	_push_recent_client(path)
	# Actualizar dir inicial de los diálogos para esta sesión
	_file_dialog_client_folder.current_dir = path
	_file_dialog_init.current_dir = path
	_file_dialog_ind.current_dir = path
	_file_dialog_folder.current_dir = path

	# Detectar subcarpetas
	var try_graficos := [
		path.path_join("Data/Graficos"),
		path.path_join("Graficos"),
	]
	var try_init := [
		path.path_join("Data/INIT"),
		path.path_join("INIT"),
	]

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
		_update_status("⚠ No se encontraron subcarpetas Graficos/ ni INIT/ en: " + path)
		return

	var msgs: PackedStringArray = PackedStringArray()

	# Cargar Graficos.ind
	if not _init_folder.is_empty():
		var grh_path := _init_folder.path_join("Graficos.ind")
		if FileAccess.file_exists(grh_path):
			_grh_data = GrhIO.load_ind(grh_path)
			_ind_path = grh_path
			_next_grh_index = _grh_data["max_index"] + 1
			_spin_next_grh.value = _next_grh_index
			_lbl_max_grh.text = "Max GrhIdx: %d  (%d entradas)" % [
				_grh_data["max_index"], _grh_data["entries"].size()]
			msgs.append("Graficos.ind: %d GRHs" % _grh_data["entries"].size())

		# Cargar Personajes.ind
		var body_path := _init_folder.path_join("Personajes.ind")
		if FileAccess.file_exists(body_path):
			_bodies_data = _load_personajes_ind(body_path)
			msgs.append("Personajes.ind: %d cuerpos" % _bodies_data.size())

		# Cargar Fxs.ind
		var fxs_path := _init_folder.path_join("Fxs.ind")
		if FileAccess.file_exists(fxs_path):
			_load_fxs_ind(fxs_path)
			msgs.append("Fxs.ind: %d efectos" % _fxs_data.size())

		# Poblar lista de archivos INIT
		_init_files.clear()
		if _init_file_list != null:
			_init_file_list.clear()
		var dir := DirAccess.open(_init_folder)
		if dir:
			dir.list_dir_begin()
			var f := dir.get_next()
			while f != "":
				if not dir.current_is_dir():
					var fp := _init_folder.path_join(f)
					_init_files.append(fp)
					if _init_file_list != null:
						_init_file_list.add_item(f)
				f = dir.get_next()
			dir.list_dir_end()
		msgs.append("INIT: %d archivos" % _init_files.size())

	# Cargar lista de imágenes GRAFICOS
	if not _graficos_folder_path.is_empty():
		_load_folder(_graficos_folder_path)
		msgs.append("Graficos: %d imágenes" % _image_files.size())

	_update_status("✅ Cliente cargado: " + " | ".join(msgs))


# ── GRH Viewer ────────────────────────────────────────────────────────────────

func _get_grh_entries_for_file_num(file_num: int) -> Array:
	var result: Array = []
	for key in _grh_data["entries"]:
		var e: Dictionary = _grh_data["entries"][key]
		if e.get("num_frames", 1) == 1 and e.get("file_num", -1) == file_num:
			result.append(e)
	result.sort_custom(func(a, b): return a.grh_index < b.grh_index)
	return result


func _refresh_grh_viewer() -> void:
	if _grh_viewer_vbox == null:
		return
	for child in _grh_viewer_vbox.get_children():
		child.free()

	# ── Estáticos ────────────────────────────────────────────────────────────
	var anim_entries := _get_animated_grh_for_file_num(_current_file_num)

	if _lbl_grh_count != null:
		_lbl_grh_count.text = "FileNum %d — %d estáticos | %d animados" % [
			_current_file_num, _current_frames.size(), anim_entries.size()]

	var lbl_static := Label.new()
	lbl_static.text = "── Frames estáticos ──"
	lbl_static.add_theme_font_size_override("font_size", 11)
	lbl_static.add_theme_color_override("font_color", Color(0.7, 0.9, 0.7))
	_grh_viewer_vbox.add_child(lbl_static)

	var limit := mini(_current_frames.size(), 300)
	for i in range(limit):
		var row := _make_grh_viewer_row(i, _current_frames[i])
		_grh_viewer_vbox.add_child(row)
	if _current_frames.size() > 300:
		var lbl := Label.new()
		lbl.text = "... y %d más" % (_current_frames.size() - 300)
		lbl.add_theme_font_size_override("font_size", 10)
		_grh_viewer_vbox.add_child(lbl)

	# ── Animados ─────────────────────────────────────────────────────────────
	if not anim_entries.is_empty():
		var lbl_anim := Label.new()
		lbl_anim.text = "── Animaciones que usan esta imagen ──"
		lbl_anim.add_theme_font_size_override("font_size", 11)
		lbl_anim.add_theme_color_override("font_color", Color(0.9, 0.7, 1.0))
		_grh_viewer_vbox.add_child(lbl_anim)

		for ae in anim_entries:
			var row := _make_anim_grh_viewer_row(ae)
			_grh_viewer_vbox.add_child(row)


func _get_animated_grh_for_file_num(file_num: int) -> Array:
	if file_num <= 0 or _grh_data["entries"].is_empty():
		return []
	# Construir set de GrhIndex estáticos para este file_num
	var static_set: Dictionary = {}
	for key in _grh_data["entries"]:
		var e: Dictionary = _grh_data["entries"][key]
		if e.get("num_frames", 1) == 1 and e.get("file_num", -1) == file_num:
			static_set[e.grh_index] = true
	if static_set.is_empty():
		return []
	# Buscar animados que referencian esos estáticos
	var result: Array = []
	for key in _grh_data["entries"]:
		var e: Dictionary = _grh_data["entries"][key]
		if e.get("num_frames", 1) > 1:
			for fi in e.get("frames", []):
				if fi in static_set:
					result.append(e)
					break
	result.sort_custom(func(a, b): return a.grh_index < b.grh_index)
	return result


func _make_anim_grh_viewer_row(e: Dictionary) -> Button:
	var nf: int = e.get("num_frames", 0)
	var speed: float = e.get("speed", 0.0)
	var frames: Array = e.get("frames", [])
	var btn := Button.new()
	btn.flat = true
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.add_theme_font_size_override("font_size", 10)
	btn.add_theme_color_override("font_color", Color(0.85, 0.65, 1.0))
	var preview_count := mini(8, frames.size())
	var frame_preview := PackedStringArray()
	for k in range(preview_count):
		frame_preview.append(str(frames[k]))
	var ellipsis := "..." if frames.size() > 8 else ""
	btn.text = "G%-6d  anim  %d frames  @%.2f spd\n  [%s%s]" % [
		e.grh_index, nf, speed, ", ".join(frame_preview), ellipsis]
	# Conectar para cargar los frames animados en el preview
	btn.pressed.connect(func():
		# Mostrar en el preview todos los frames de la animación
		var anim_frames: Array = []
		for fi in frames:
			var se = _grh_data["entries"].get(fi, null)
			if se != null and se.get("num_frames", 1) == 1:
				anim_frames.append({
					"sx": se.get("sx", 0), "sy": se.get("sy", 0),
					"w": se.get("width", 32), "h": se.get("height", 32),
					"grh_index": fi, "file_num": se.get("file_num", 0)
				})
		if not anim_frames.is_empty():
			_preview.set_frames(anim_frames)
			_preview.show_frame(0)
			_anim_frame_idx = 0
			_anim_fps = speed
			if _spin_anim_fps != null:
				_spin_anim_fps.set_value_no_signal(speed)
			_update_status("GRH Animado G%d — %d frames @ %.2f spd" % [e.grh_index, nf, speed]))
	return btn


func _make_grh_viewer_row(idx: int, f: Dictionary) -> Button:
	var btn := Button.new()
	btn.flat = true
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.custom_minimum_size.y = 52
	btn.text = "G%-6d  (%d, %d)  %d × %d" % [f.grh_index, f.sx, f.sy, f.w, f.h]
	btn.add_theme_font_size_override("font_size", 10)
	if _current_texture != null and f.w > 0 and f.h > 0:
		var atlas := AtlasTexture.new()
		atlas.atlas = _current_texture
		atlas.region = Rect2(float(f.sx), float(f.sy), float(f.w), float(f.h))
		btn.icon = atlas
		btn.icon_alignment = HORIZONTAL_ALIGNMENT_LEFT
		btn.add_theme_constant_override("icon_max_width", 48)
	var i := idx
	btn.pressed.connect(func(): _on_frame_row_selected(i))
	btn.mouse_entered.connect(func():
		if i >= 0 and i < _current_frames.size():
			var ff: Dictionary = _current_frames[i]
			_update_status("G%d — (%d,%d) %d×%d" % [ff.grh_index, ff.sx, ff.sy, ff.w, ff.h]))
	return btn


# ── Editor de archivos INIT ───────────────────────────────────────────────────

func _on_init_file_selected(list_idx: int) -> void:
	if list_idx < 0 or list_idx >= _init_files.size():
		return
	_load_init_file_to_editor(_init_files[list_idx])


func _load_init_file_to_editor(path: String) -> void:
	_init_current_file_path = path
	var ext := path.get_extension().to_lower()
	var fname := path.get_file().to_lower()

	# Determinar si es binario o texto
	if fname == "graficos.ind":
		if _grh_data["entries"].size() > 0:
			_init_text_edit.text = GrhIO.to_text(_grh_data)
		else:
			_init_text_edit.text = "# Graficos.ind vacío o no cargado"
		_init_text_edit.editable = true
	elif fname == "personajes.ind":
		_init_text_edit.text = _personajes_to_text()
		_init_text_edit.editable = false
	elif ext in ["ini", "ao", "dat", "txt"]:
		# Archivo de texto
		var f := FileAccess.open(path, FileAccess.READ)
		if f != null:
			_init_text_edit.text = f.get_as_text()
			f.close()
			_init_text_edit.editable = true
		else:
			_init_text_edit.text = "# Error al leer: " + path
	elif ext == "ind":
		# Otro binario .ind — mostrar info básica
		var f := FileAccess.open(path, FileAccess.READ)
		if f != null:
			var size := f.get_length()
			f.close()
			_init_text_edit.text = "# %s — archivo binario (%d bytes)\n# Vista de texto no disponible para este formato." % [path.get_file(), size]
			_init_text_edit.editable = false
		else:
			_init_text_edit.text = "# No se pudo leer: " + path
	else:
		var f := FileAccess.open(path, FileAccess.READ)
		if f != null:
			_init_text_edit.text = f.get_as_text()
			f.close()
			_init_text_edit.editable = true
		else:
			_init_text_edit.text = "# No se pudo leer: " + path

	_update_status("📝 %s" % path.get_file())


func _save_init_file() -> void:
	if _init_current_file_path.is_empty():
		_update_status("⚠ No hay archivo INIT activo para guardar.")
		return
	var fname := _init_current_file_path.get_file().to_lower()
	if fname == "graficos.ind":
		# Parsear texto editado y guardar como binario
		var parsed := GrhIO.from_text(_init_text_edit.text)
		if parsed["entries"].size() == 0:
			_update_status("⚠ El texto no contiene entradas válidas.")
			return
		_grh_data = parsed
		_grh_data["version"] = 12
		if GrhIO.save_ind(_init_current_file_path, _grh_data):
			_update_status("✅ Graficos.ind guardado (%d entradas)" % _grh_data["entries"].size())
		else:
			_update_status("❌ Error al guardar Graficos.ind")
	elif not _init_text_edit.editable:
		_update_status("⚠ Este archivo no es editable en modo texto.")
	else:
		var f := FileAccess.open(_init_current_file_path, FileAccess.WRITE)
		if f != null:
			f.store_string(_init_text_edit.text)
			f.close()
			_update_status("✅ Guardado: " + _init_current_file_path.get_file())
		else:
			_update_status("❌ No se pudo escribir: " + _init_current_file_path)


func _personajes_to_text() -> String:
	if _bodies_data.is_empty():
		return "# Personajes.ind no cargado o vacío"
	var lines := PackedStringArray()
	lines.append("# Personajes.ind — %d cuerpos" % _bodies_data.size())
	lines.append("# Idx WalkN WalkE WalkS WalkW HeadOffX HeadOffY")
	for b in _bodies_data:
		lines.append("%d %d %d %d %d %d %d" % [
			b.index, b.walk_n, b.walk_e, b.walk_s, b.walk_w, b.head_x, b.head_y])
	return "\n".join(lines)


func _on_create_anim_grh() -> void:
	var raw := _edit_anim_indices.text.strip_edges()
	if raw.is_empty():
		_update_status("⚠ Escribe los GrhIndex de los frames para la animación.")
		return

	# Parsear índices (separados por coma, espacio o ambos)
	var parts := raw.split(",")
	var frame_indices: Array[int] = []
	for p in parts:
		var s := p.strip_edges()
		if s.is_valid_int():
			frame_indices.append(s.to_int())

	if frame_indices.size() < 2:
		_update_status("⚠ Se necesitan al menos 2 frames para una animación.")
		return

	# Verificar que todos los índices existen como entradas estáticas
	var missing: Array[int] = []
	for fi in frame_indices:
		var e = _grh_data["entries"].get(fi, null)
		if e == null or e.get("num_frames", 1) != 1:
			missing.append(fi)
	if not missing.is_empty():
		_update_status("⚠ GrhIndex no encontrados como estáticos: %s" % str(missing))
		return

	var speed: float = _spin_anim_speed.value
	var new_idx := _next_grh_index
	_next_grh_index += 1
	_spin_next_grh.value = _next_grh_index

	var entry := {
		"grh_index": new_idx,
		"num_frames": frame_indices.size(),
		"frames": frame_indices,
		"speed": speed
	}
	_grh_data["entries"][new_idx] = entry
	if new_idx > _grh_data["max_index"]:
		_grh_data["max_index"] = new_idx

	_lbl_max_grh.text = "Max GrhIdx: %d  (%d entradas)" % [
		_grh_data["max_index"], _grh_data["entries"].size()]
	_update_status("✅ GRH Animado G%d creado: %d frames @ %.2f speed" % [
		new_idx, frame_indices.size(), speed])
	_edit_anim_indices.text = ""
	_refresh_grh_viewer()


func _save_personajes_ind() -> void:
	if _bodies_data.is_empty():
		_update_status("⚠ No hay datos de Personajes.ind cargados.")
		return
	var path := _init_folder.path_join("Personajes.ind")
	if not FileAccess.file_exists(path):
		_update_status("⚠ No se encontró Personajes.ind en: " + _init_folder)
		return
	# Leer MiCabecera original
	var orig := FileAccess.open(path, FileAccess.READ)
	if orig == null:
		_update_status("❌ No se pudo leer Personajes.ind")
		return
	var header: PackedByteArray = orig.get_buffer(263)
	orig.close()
	# Escribir nuevo archivo
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("❌ No se pudo escribir Personajes.ind")
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
	_update_status("✅ Personajes.ind guardado (%d cuerpos)" % _bodies_data.size())


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
		var entry := {
			"index": i,
			"animacion": f.get_16(),
			"offset_x": f.get_16(),
			"offset_y": f.get_16()
		}
		_fxs_data.append(entry)
	f.close()


func _save_fxs_ind() -> void:
	if _fxs_data.is_empty():
		_update_status("⚠ No hay datos de Fxs.ind cargados.")
		return
	var path := _init_folder.path_join("Fxs.ind")
	if not FileAccess.file_exists(path):
		_update_status("⚠ No se encontró Fxs.ind en: " + _init_folder)
		return
	# Preservar MiCabecera original si no la tenemos ya
	if _mi_cabecera_fxs.is_empty():
		var orig := FileAccess.open(path, FileAccess.READ)
		if orig != null:
			_mi_cabecera_fxs = orig.get_buffer(263)
			orig.close()
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_update_status("❌ No se pudo escribir Fxs.ind")
		return
	f.store_buffer(_mi_cabecera_fxs)
	f.store_16(_fxs_data.size())
	for e in _fxs_data:
		f.store_16(e.animacion & 0xFFFF)
		f.store_16(e.offset_x & 0xFFFF)
		f.store_16(e.offset_y & 0xFFFF)
	f.close()
	_update_status("✅ Fxs.ind guardado (%d efectos)" % _fxs_data.size())


# ── Helpers de UI ─────────────────────────────────────────────────────────────

func _update_status(msg: String) -> void:
	_lbl_status.text = msg
	print(msg)


func _btn(text: String, callback: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.pressed.connect(callback)
	return b


func _label(text: String) -> Label:
	var l := Label.new()
	l.text = text
	return l


func _heading(text: String) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 13)
	l.add_theme_color_override("font_color", Color(0.9, 0.9, 0.5))
	return l


func _spinbox(min_val: float, max_val: float, default: float, callback = null) -> SpinBox:
	var s := SpinBox.new()
	s.min_value = min_val
	s.max_value = max_val
	s.value = default
	s.custom_minimum_size.x = 72
	s.allow_greater = false
	if callback != null:
		s.value_changed.connect(callback)
	return s
