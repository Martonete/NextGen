## SpriteCanvas.gd — Canvas interactivo con hover-detection de sprites

class_name SpriteCanvas
extends Control

signal frame_drawn(rect: Rect2)                     # Frame dibujado manualmente
signal frame_selected(index: int)                   # Frame existente clickeado (-1 = deselect)
signal multi_frame_selected(indices: Array)          # Multi-select via CTRL+click
signal blob_clicked(rect: Rect2i)                   # Blob hover clickeado para agregar
signal frame_resized(index: int, new_rect: Rect2)   # Frame redimensionado via handles
signal frame_delete_pressed(index: int)             # Tecla Delete sobre frame seleccionado
signal frame_context_menu(index: int, screen_pos: Vector2)  # Right-click on frame

# ── Imagen ───────────────────────────────────────────────────────────────────

var _texture: ImageTexture = null
var _image_size := Vector2.ZERO

# ── Blob map (pre-computado al cargar imagen) ─────────────────────────────────

var _blob_map: PackedInt32Array = PackedInt32Array()
var _blob_rects: Array = []             # Array of Rect2i
var _blob_id_to_rect: PackedInt32Array = PackedInt32Array()
var _blob_map_w: int = 0
var _hover_rect: Rect2i = Rect2i()      # Rect del blob bajo el cursor (vacío=nada)

# ── Content regions (smart mode) ──────────────────────────────────────────────
var _content_regions: Array = []         # Array of Rect2i from detect_content_rows


# ── Frames definidos ──────────────────────────────────────────────────────────

var _frames: Array = []
var _selected_frame: int = -1
var _selected_frames: Array[int] = []

# ── Tool mode (0=Select+Draw, 1=Pan) ────────────────────────────────────────

var tool_mode: int = 0

# ── Zoom / Pan ────────────────────────────────────────────────────────────────

var _zoom := 1.0
var _pan  := Vector2.ZERO
var _panning  := false
var _pan_start := Vector2.ZERO
var _pan_start_offset := Vector2.ZERO

# ── Dibujo manual ────────────────────────────────────────────────────────────

var _drawing    := false
var _draw_start_img  := Vector2.ZERO
var _draw_cur_img    := Vector2.ZERO
var _press_pos_screen := Vector2.ZERO   # para distinguir click vs drag
const DRAG_THRESHOLD := 5.0

# ── Resize handles ────────────────────────────────────────────────────────────
# Handles: 0=TL 1=T 2=TR 3=R 4=BR 5=B 6=BL 7=L

const HANDLE_R := 6.0      # radio en pixels de pantalla
var _resize_active: bool = false
var _resize_handle: int = -1
var _resize_frame_orig: Rect2 = Rect2()
var _resize_mouse_start_img: Vector2 = Vector2.ZERO
var _resize_live: Rect2 = Rect2()   # rect en imagen durante el drag

# ── Mover frame ───────────────────────────────────────────────────────────────

var _move_active: bool = false
var _move_frame_orig: Rect2 = Rect2()
var _move_mouse_start_img: Vector2 = Vector2.ZERO
var _move_live_pos: Vector2 = Vector2.ZERO  # top-left en imagen durante el drag

# ── Frame detection (hover sprite detection + tile snap) ────────────────────

var detect_enabled: bool = true

# ── Grid overlay (visual + draw snapping) ────────────────────────────────────

var show_grid: bool = false
var grid_cell_w: int = 128    # major grid cell width
var grid_cell_h: int = 128    # major grid cell height
var grid_line_width: float = 1.0    # line thickness in pixels
var grid_color: Color = Color(1.0, 0.85, 0.0)  # base color (alpha computed per line type)
const GRID_TILE: int = 32     # minor grid subdivision (always 32px)

# ── Visibility toggle ────────────────────────────────────────────────────────

var show_frames: bool = true

func set_detect(enabled: bool) -> void:
	detect_enabled = enabled
	_hover_rect = Rect2i()
	queue_redraw()


func set_grid_visible(visible: bool) -> void:
	show_grid = visible
	queue_redraw()


func set_grid_cell(cw: int, ch: int) -> void:
	grid_cell_w = maxi(cw, 8)
	grid_cell_h = maxi(ch, 8)
	queue_redraw()


func set_grid_line_w(w: float) -> void:
	grid_line_width = maxf(w, 0.5)
	queue_redraw()


func set_grid_color(col: Color) -> void:
	grid_color = col
	queue_redraw()


## Snap a rect to tile grid (32px boundaries), expanding to contain content.
## Used by Smart mode hover detection to align detected sprites to tile grid.
func _snap_to_tile_grid(rect: Rect2i) -> Rect2i:
	var t: int = GRID_TILE
	# Floor position to tile boundary
	@warning_ignore("integer_division")
	var new_x: int = (rect.position.x / t) * t
	@warning_ignore("integer_division")
	var new_y: int = (rect.position.y / t) * t
	# Ceil right/bottom edge to tile boundary
	var right_edge: int = rect.position.x + rect.size.x
	var bottom_edge: int = rect.position.y + rect.size.y
	@warning_ignore("integer_division")
	var new_w: int = maxi(t, ((right_edge + t - 1) / t) * t - new_x)
	@warning_ignore("integer_division")
	var new_h: int = maxi(t, ((bottom_edge + t - 1) / t) * t - new_y)

	# Clamp to image borders
	if _image_size.x > 0:
		var img_w: int = int(_image_size.x)
		var img_h: int = int(_image_size.y)
		new_x = clampi(new_x, 0, img_w - 1)
		new_y = clampi(new_y, 0, img_h - 1)
		new_w = mini(new_w, img_w - new_x)
		new_h = mini(new_h, img_h - new_y)

	return Rect2i(new_x, new_y, new_w, new_h)


# ── Colores ───────────────────────────────────────────────────────────────────

const FRAME_COLORS := [
	Color(1.0, 0.35, 0.35, 1.0),
	Color(0.35, 1.0, 0.35, 1.0),
	Color(0.35, 0.55, 1.0, 1.0),
	Color(1.0, 0.90, 0.20, 1.0),
	Color(0.85, 0.35, 1.0, 1.0),
	Color(0.25, 0.95, 0.85, 1.0),
	Color(1.0, 0.55, 0.15, 1.0),
]
const CHECKER_SIZE := 12.0
const COL_CHECKER_A := Color(0.67, 0.67, 0.67)
const COL_CHECKER_B := Color(0.45, 0.45, 0.45)
const COL_HOVER_FILL   := Color(1.0, 0.85, 0.0, 0.18)
const COL_HOVER_BORDER := Color(1.0, 0.85, 0.0, 0.95)


# ── API pública ───────────────────────────────────────────────────────────────

func load_image(img: Image) -> void:
	_texture = ImageTexture.create_from_image(img)
	_image_size = Vector2(img.get_width(), img.get_height())
	_selected_frame = -1
	_selected_frames.clear()
	_drawing = false
	_hover_rect = Rect2i()
	_blob_map = PackedInt32Array()
	_blob_rects = []
	_content_regions = []
	# Diferir fit para que el canvas tenga su tamaño definitivo tras el layout
	call_deferred("fit_to_canvas")
	queue_redraw()


func set_blob_data(map: PackedInt32Array, rects: Array, id_to_rect: PackedInt32Array, w: int) -> void:
	_blob_map = map
	_blob_rects = rects
	_blob_id_to_rect = id_to_rect
	_blob_map_w = w
	_hover_rect = Rect2i()
	queue_redraw()


func set_content_regions(regions: Array) -> void:
	_content_regions = regions


func set_frames(frames: Array) -> void:
	_frames = frames
	queue_redraw()


func set_selected(index: int) -> void:
	_selected_frame = index
	queue_redraw()


func clear_image() -> void:
	_texture = null
	_image_size = Vector2.ZERO
	_frames = []
	_selected_frame = -1
	_selected_frames.clear()
	_blob_map = PackedInt32Array()
	_blob_rects = []
	_content_regions = []
	_hover_rect = Rect2i()
	queue_redraw()


func fit_to_canvas() -> void:
	if _image_size == Vector2.ZERO or size == Vector2.ZERO: return
	# Fit image filling ~80% of canvas (5% margin each side)
	var margin_pct := 0.05
	var usable_w := size.x * (1.0 - margin_pct * 2)
	var usable_h := size.y * (1.0 - margin_pct * 2)
	_zoom = minf(usable_w / _image_size.x, usable_h / _image_size.y)
	# Cap max zoom so tiny images don't appear enormous
	_zoom = minf(_zoom, 2.0)
	_pan = Vector2(
		(size.x - _image_size.x * _zoom) * 0.5,
		(size.y - _image_size.y * _zoom) * 0.5
	)
	queue_redraw()


func zoom_in()    -> void: _zoom = minf(_zoom * 1.2, 32.0); queue_redraw()
func zoom_out()   -> void: _zoom = maxf(_zoom / 1.2, 0.02); queue_redraw()
func zoom_reset() -> void: _zoom = 1.0; _pan = (size - _image_size) * 0.5; queue_redraw()


# ── Coordenadas ───────────────────────────────────────────────────────────────

func _s2i(p: Vector2) -> Vector2: return (p - _pan) / _zoom
func _i2s(p: Vector2) -> Vector2: return p * _zoom + _pan
func _irect2srect(r: Rect2) -> Rect2: return Rect2(_i2s(r.position), r.size * _zoom)


# ── Resize handles helpers ────────────────────────────────────────────────────

func _handle_positions(sr: Rect2) -> Array:
	var p: Vector2 = sr.position
	var s: Vector2 = sr.size
	return [
		p,                                   # 0 TL
		p + Vector2(s.x * 0.5, 0.0),        # 1 T
		p + Vector2(s.x, 0.0),              # 2 TR
		p + Vector2(s.x, s.y * 0.5),        # 3 R
		p + s,                               # 4 BR
		p + Vector2(s.x * 0.5, s.y),        # 5 B
		p + Vector2(0.0, s.y),              # 6 BL
		p + Vector2(0.0, s.y * 0.5),        # 7 L
	]


func _hit_handles(screen_pos: Vector2) -> int:
	if _selected_frame < 0 or _selected_frame >= _frames.size():
		return -1
	var fr: Dictionary = _frames[_selected_frame]
	var sr := _irect2srect(Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h)))
	var handles := _handle_positions(sr)
	for i in range(handles.size()):
		var h: Vector2 = handles[i]
		if screen_pos.distance_to(h) <= HANDLE_R + 3.0:
			return i
	return -1


func _resize_rect_from_drag(handle: int, delta_img: Vector2) -> Rect2:
	var x1: float = _resize_frame_orig.position.x
	var y1: float = _resize_frame_orig.position.y
	var x2: float = x1 + _resize_frame_orig.size.x
	var y2: float = y1 + _resize_frame_orig.size.y
	match handle:
		0: x1 += delta_img.x; y1 += delta_img.y  # TL
		1: y1 += delta_img.y                       # T
		2: x2 += delta_img.x; y1 += delta_img.y  # TR
		3: x2 += delta_img.x                       # R
		4: x2 += delta_img.x; y2 += delta_img.y  # BR
		5: y2 += delta_img.y                       # B
		6: x1 += delta_img.x; y2 += delta_img.y  # BL
		7: x1 += delta_img.x                       # L
	var nx1 := minf(x1, x2 - 1.0)
	var ny1 := minf(y1, y2 - 1.0)
	var nx2 := maxf(x2, x1 + 1.0)
	var ny2 := maxf(y2, y1 + 1.0)
	# Snap edges to grid tile boundaries
	if show_grid:
		var t := float(GRID_TILE)
		match handle:
			0: nx1 = floorf(nx1 / t) * t; ny1 = floorf(ny1 / t) * t
			1: ny1 = floorf(ny1 / t) * t
			2: nx2 = ceilf(nx2 / t) * t; ny1 = floorf(ny1 / t) * t
			3: nx2 = ceilf(nx2 / t) * t
			4: nx2 = ceilf(nx2 / t) * t; ny2 = ceilf(ny2 / t) * t
			5: ny2 = ceilf(ny2 / t) * t
			6: nx1 = floorf(nx1 / t) * t; ny2 = ceilf(ny2 / t) * t
			7: nx1 = floorf(nx1 / t) * t
	# Clamp to image borders
	if _image_size.x > 0:
		nx1 = maxf(nx1, 0.0)
		ny1 = maxf(ny1, 0.0)
		nx2 = minf(nx2, _image_size.x)
		ny2 = minf(ny2, _image_size.y)
	return Rect2(nx1, ny1, nx2 - nx1, ny2 - ny1)


# ── Dibujado ──────────────────────────────────────────────────────────────────

func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), Color(0.15, 0.15, 0.15))

	if _texture == null:
		draw_string(ThemeDB.fallback_font, size * 0.5 - Vector2(120, 0),
			"Selecciona una imagen de la lista",
			HORIZONTAL_ALIGNMENT_LEFT, -1, 14, Color(0.6, 0.6, 0.6))
		return

	# Checkerboard
	var img_sw := _image_size.x * _zoom
	var img_sh := _image_size.y * _zoom
	var img_sr := Rect2(_pan, Vector2(img_sw, img_sh))
	_draw_checker(img_sr)

	# Imagen
	draw_texture_rect(_texture, img_sr, false)
	draw_rect(img_sr, Color(0.4, 0.4, 0.4), false, 1.0)

	# Grid overlay (independent of snap mode)
	if show_grid and _zoom >= 0.15:
		_draw_grid(img_sr)

	# Hover blob highlight (solo si no solapa frames existentes)
	if _hover_rect.size.x > 0 and not _overlaps_any_frame(Rect2(_hover_rect.position, _hover_rect.size)):
		var hr := Rect2(_hover_rect.position, _hover_rect.size)
		var sr := _irect2srect(hr)
		draw_rect(sr, COL_HOVER_FILL)
		draw_rect(sr, COL_HOVER_BORDER, false, 2.0)
		# Label "Click para agregar"
		if sr.size.x > 60:
			draw_string(ThemeDB.fallback_font, sr.position + Vector2(4, 14),
				"Click p/ agregar  %d x %d" % [_hover_rect.size.x, _hover_rect.size.y],
				HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(1, 1, 0.5, 0.9))

	# Frames existentes (skip if toggled off in Ver menu)
	if show_frames:
		for i in range(_frames.size()):
			var fr: Dictionary = _frames[i]
			var col: Color = FRAME_COLORS[i % FRAME_COLORS.size()]
			var is_sel := (i == _selected_frame)
			var is_multi := _selected_frames.has(i)
			# Durante resize/move activo, usar el rect live para el frame seleccionado
			var draw_r: Rect2
			if is_sel and _resize_active:
				draw_r = _resize_live
			elif is_sel and _move_active:
				draw_r = Rect2(_move_live_pos, _move_frame_orig.size)
			else:
				draw_r = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
			var sr := _irect2srect(draw_r)
			draw_rect(sr, Color(col.r, col.g, col.b, 0.15 if is_sel else (0.10 if is_multi else 0.06)))
			if is_sel:
				draw_rect(sr, Color.WHITE, false, 2.0)
			elif is_multi:
				draw_rect(sr, Color(0.3, 0.7, 1.0), false, 2.0)
			else:
				draw_rect(sr, col, false, 1.0)
			if sr.size.x > 22:
				var lbl: String
				if is_sel and _resize_active:
					lbl = "%d×%d" % [int(draw_r.size.x), int(draw_r.size.y)]
				else:
					lbl = "G%d" % fr.get("grh_index", 0)
				draw_string(ThemeDB.fallback_font, sr.position + Vector2(3, 14),
					lbl, HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(1, 1, 1, 0.85))

	# Handles del frame seleccionado (dibujados encima)
	if show_frames and _selected_frame >= 0 and _selected_frame < _frames.size():
		var fr: Dictionary = _frames[_selected_frame]
		var draw_r: Rect2
		if _resize_active:
			draw_r = _resize_live
		elif _move_active:
			draw_r = Rect2(_move_live_pos, _move_frame_orig.size)
		else:
			draw_r = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
		var sr := _irect2srect(draw_r)
		var handles := _handle_positions(sr)
		for i in range(handles.size()):
			var h: Vector2 = handles[i]
			var hs: float = HANDLE_R if i % 2 == 0 else HANDLE_R * 0.75
			draw_rect(Rect2(h - Vector2(hs, hs), Vector2(hs * 2.0, hs * 2.0)), Color(0.0, 0.0, 0.0, 0.75))
			draw_rect(Rect2(h - Vector2(hs, hs), Vector2(hs * 2.0, hs * 2.0)), Color(1.0, 1.0, 1.0, 0.95), false, 1.5)
		# Info durante resize / move
		if _resize_active:
			var info := "%d × %d px" % [int(draw_r.size.x), int(draw_r.size.y)]
			draw_string(ThemeDB.fallback_font, sr.position + Vector2(0.0, -4.0),
				info, HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(1.0, 1.0, 0.3, 0.95))
		elif _move_active:
			var info := "(%d, %d)" % [int(draw_r.position.x), int(draw_r.position.y)]
			draw_string(ThemeDB.fallback_font, sr.position + Vector2(0.0, -4.0),
				info, HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(0.4, 1.0, 1.0, 0.95))

	# Rubber band manual
	if _drawing:
		var ix := minf(_draw_start_img.x, _draw_cur_img.x)
		var iy := minf(_draw_start_img.y, _draw_cur_img.y)
		var iw := absf(_draw_cur_img.x - _draw_start_img.x)
		var ih := absf(_draw_cur_img.y - _draw_start_img.y)
		if show_grid and iw >= 2.0 and ih >= 2.0:
			# Snap to tile grid while drawing
			var snapped := _snap_to_tile_grid(Rect2i(int(ix), int(iy), int(iw), int(ih)))
			var sr := _irect2srect(Rect2(snapped.position, snapped.size))
			draw_rect(sr, Color(0.3, 1.0, 0.3, 0.15))
			draw_rect(sr, Color(0.3, 1.0, 0.3, 0.9), false, 2.0)
			draw_string(ThemeDB.fallback_font, sr.position + Vector2(4, 14),
				"%d x %d" % [snapped.size.x, snapped.size.y],
				HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(0.3, 1.0, 0.3, 0.95))
		else:
			var ds := _i2s(_draw_start_img)
			var dc := _i2s(_draw_cur_img)
			var rb := Rect2(Vector2(minf(ds.x, dc.x), minf(ds.y, dc.y)),
				Vector2(absf(dc.x - ds.x), absf(dc.y - ds.y)))
			draw_rect(rb, Color(1, 1, 1, 0.12))
			draw_rect(rb, Color.WHITE, false, 1.5)

	# Info zoom
	draw_string(ThemeDB.fallback_font, Vector2(6, size.y - 6),
		"Zoom %.0f%%  |  %d x %d px" % [_zoom * 100, _image_size.x, _image_size.y],
		HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(0.6, 0.6, 0.6))


func _draw_checker(rect: Rect2) -> void:
	var cols := ceili(rect.size.x / CHECKER_SIZE)
	var rows := ceili(rect.size.y / CHECKER_SIZE)
	for row in range(rows):
		for col in range(cols):
			var cc := COL_CHECKER_A if (col + row) % 2 == 0 else COL_CHECKER_B
			draw_rect(Rect2(
				rect.position.x + col * CHECKER_SIZE,
				rect.position.y + row * CHECKER_SIZE,
				minf(CHECKER_SIZE, rect.size.x - col * CHECKER_SIZE),
				minf(CHECKER_SIZE, rect.size.y - row * CHECKER_SIZE)
			), cc)


func _draw_grid(img_sr: Rect2) -> void:
	var tile_step := float(GRID_TILE) * _zoom
	# Skip drawing if grid lines would be too dense (< 4px apart)
	if tile_step < 4.0:
		return
	# Determine visible area
	var vis_left := maxf(img_sr.position.x, 0.0)
	var vis_top := maxf(img_sr.position.y, 0.0)
	var vis_right := minf(img_sr.position.x + img_sr.size.x, size.x)
	var vis_bottom := minf(img_sr.position.y + img_sr.size.y, size.y)
	# Colors derived from grid_color with different alpha
	var col_minor := Color(grid_color.r, grid_color.g, grid_color.b, 0.35)
	var col_major := Color(grid_color.r, grid_color.g, grid_color.b, 0.7)
	var w_minor := grid_line_width
	var w_major := grid_line_width * 2.0
	# How many tiles per major cell
	@warning_ignore("integer_division")
	var tiles_per_cell_x: int = maxi(1, grid_cell_w / GRID_TILE)
	@warning_ignore("integer_division")
	var tiles_per_cell_h: int = maxi(1, grid_cell_h / GRID_TILE)
	# Vertical lines
	@warning_ignore("integer_division")
	var first_col := int((vis_left - img_sr.position.x) / tile_step)
	var total_cols := ceili(_image_size.x / float(GRID_TILE))
	for i in range(first_col, total_cols + 1):
		var sx := img_sr.position.x + float(i) * tile_step
		if sx < vis_left or sx > vis_right:
			continue
		var is_major := (i % tiles_per_cell_x == 0)
		draw_line(Vector2(sx, vis_top), Vector2(sx, vis_bottom),
			col_major if is_major else col_minor,
			w_major if is_major else w_minor)
	# Horizontal lines
	@warning_ignore("integer_division")
	var first_row := int((vis_top - img_sr.position.y) / tile_step)
	var total_rows := ceili(_image_size.y / float(GRID_TILE))
	for i in range(first_row, total_rows + 1):
		var sy := img_sr.position.y + float(i) * tile_step
		if sy < vis_top or sy > vis_bottom:
			continue
		var is_major := (i % tiles_per_cell_h == 0)
		draw_line(Vector2(vis_left, sy), Vector2(vis_right, sy),
			col_major if is_major else col_minor,
			w_major if is_major else w_minor)


# ── Input ─────────────────────────────────────────────────────────────────────

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		_on_mouse_button(event as InputEventMouseButton)
	elif event is InputEventMouseMotion:
		_on_mouse_motion(event as InputEventMouseMotion)
	elif event is InputEventKey:
		_on_key(event as InputEventKey)


func _on_key(k: InputEventKey) -> void:
	if not k.pressed:
		return
	match k.keycode:
		KEY_ESCAPE:
			if _selected_frame >= 0 or not _selected_frames.is_empty():
				_selected_frame = -1
				_selected_frames.clear()
				frame_selected.emit(-1)
				multi_frame_selected.emit([])
				queue_redraw()
		KEY_DELETE:
			if _selected_frame >= 0:
				var idx := _selected_frame
				_selected_frame = -1
				frame_delete_pressed.emit(idx)
				queue_redraw()


func _on_mouse_button(mb: InputEventMouseButton) -> void:
	match mb.button_index:
		MOUSE_BUTTON_WHEEL_UP:
			if mb.pressed:
				var old := _zoom
				_zoom = minf(_zoom * 1.15, 32.0)
				_pan = mb.position - (mb.position - _pan) * (_zoom / old)
				accept_event()
				queue_redraw()
		MOUSE_BUTTON_WHEEL_DOWN:
			if mb.pressed:
				var old := _zoom
				_zoom = maxf(_zoom / 1.15, 0.02)
				_pan = mb.position - (mb.position - _pan) * (_zoom / old)
				accept_event()
				queue_redraw()
		MOUSE_BUTTON_MIDDLE:
			_panning = mb.pressed
			if mb.pressed:
				_pan_start = mb.position
				_pan_start_offset = _pan
		MOUSE_BUTTON_RIGHT:
			if mb.pressed and _texture != null and tool_mode == 0:
				var ip := _s2i(mb.position)
				var hit := _hit_frame(ip)
				if hit >= 0:
					frame_context_menu.emit(hit, mb.global_position)
					accept_event()
					return
			_panning = mb.pressed
			if mb.pressed:
				_pan_start = mb.position
				_pan_start_offset = _pan
		MOUSE_BUTTON_LEFT:
			if mb.pressed:
				_press_pos_screen = mb.position
				_drawing = false
				_resize_active = false
				_move_active = false
				if _texture != null:
					var ip := _s2i(mb.position)
					if tool_mode == 1:
						# PAN mode: left-click pans
						_panning = true
						_pan_start = mb.position
						_pan_start_offset = _pan
					else:
						# UNIFIED mode: handles > frame hit > draw on empty
						var handle := _hit_handles(mb.position)
						if handle >= 0:
							_resize_handle = handle
							_resize_active = true
							var fr: Dictionary = _frames[_selected_frame]
							_resize_frame_orig = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
							_resize_mouse_start_img = ip
							_resize_live = _resize_frame_orig
						else:
							var hit := _hit_test_frame(ip)
							if hit >= 0:
								if mb.ctrl_pressed:
									# CTRL+click: toggle multi-select
									var pos := _selected_frames.find(hit)
									if pos >= 0:
										_selected_frames.remove_at(pos)
									else:
										_selected_frames.append(hit)
									_selected_frame = hit
									frame_selected.emit(hit)
									multi_frame_selected.emit(_selected_frames.duplicate())
									queue_redraw()
								elif hit == _selected_frame:
									var fr: Dictionary = _frames[_selected_frame]
									_move_frame_orig = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
									_move_mouse_start_img = ip
									_move_live_pos = _move_frame_orig.position
									_move_active = true
								else:
									_selected_frame = hit
									_selected_frames = [hit] as Array[int]
									frame_selected.emit(hit)
									multi_frame_selected.emit(_selected_frames.duplicate())
									queue_redraw()
							else:
								# Empty space → deselect + start drawing
								if _selected_frame >= 0 or not _selected_frames.is_empty():
									_selected_frame = -1
									_selected_frames.clear()
									frame_selected.emit(-1)
									multi_frame_selected.emit([])
									queue_redraw()
								if show_grid:
									@warning_ignore("integer_division")
									ip.x = float((int(ip.x) / GRID_TILE) * GRID_TILE)
									@warning_ignore("integer_division")
									ip.y = float((int(ip.y) / GRID_TILE) * GRID_TILE)
								_draw_start_img = ip
								_draw_cur_img = ip
			else:
				if _panning:
					_panning = false
				elif _resize_active:
					_resize_active = false
					var delta_img := _s2i(mb.position) - _resize_mouse_start_img
					var new_rect := _resize_rect_from_drag(_resize_handle, delta_img)
					frame_resized.emit(_selected_frame, new_rect)
					queue_redraw()
				elif _move_active:
					_move_active = false
					var delta_img := _s2i(mb.position) - _move_mouse_start_img
					var new_pos := _move_frame_orig.position + delta_img
					if _image_size.x > 0:
						new_pos.x = clampf(new_pos.x, 0.0, _image_size.x - _move_frame_orig.size.x)
						new_pos.y = clampf(new_pos.y, 0.0, _image_size.y - _move_frame_orig.size.y)
					if show_grid:
						@warning_ignore("integer_division")
						new_pos.x = float((int(new_pos.x) / GRID_TILE) * GRID_TILE)
						@warning_ignore("integer_division")
						new_pos.y = float((int(new_pos.y) / GRID_TILE) * GRID_TILE)
					var new_rect := Rect2(new_pos, _move_frame_orig.size)
					frame_resized.emit(_selected_frame, new_rect)
					queue_redraw()
				elif _drawing:
					_drawing = false
					var x := minf(_draw_start_img.x, _draw_cur_img.x)
					var y := minf(_draw_start_img.y, _draw_cur_img.y)
					var w := absf(_draw_cur_img.x - _draw_start_img.x)
					var h := absf(_draw_cur_img.y - _draw_start_img.y)
					# Clamp to image borders
					if _image_size.x > 0:
						x = maxf(x, 0.0)
						y = maxf(y, 0.0)
						w = minf(w, _image_size.x - x)
						h = minf(h, _image_size.y - y)
					# Grid snap: snap edges to tile boundaries
					if show_grid and w >= 2.0 and h >= 2.0:
						@warning_ignore("integer_division")
						x = float((int(x) / GRID_TILE) * GRID_TILE)
						@warning_ignore("integer_division")
						y = float((int(y) / GRID_TILE) * GRID_TILE)
						var right_edge := x + w
						var bottom_edge := y + h
						@warning_ignore("integer_division")
						w = float(((int(right_edge) + GRID_TILE - 1) / GRID_TILE) * GRID_TILE) - x
						@warning_ignore("integer_division")
						h = float(((int(bottom_edge) + GRID_TILE - 1) / GRID_TILE) * GRID_TILE) - y
						# Clamp again after snap
						if _image_size.x > 0:
							w = minf(w, _image_size.x - x)
							h = minf(h, _image_size.y - y)
					# Smart mode: snap drawn frame to tile grid
					elif detect_enabled and w >= 2.0 and h >= 2.0:
						var snapped := _snap_to_tile_grid(Rect2i(int(x), int(y), int(w), int(h)))
						x = float(snapped.position.x)
						y = float(snapped.position.y)
						w = float(snapped.size.x)
						h = float(snapped.size.y)
					if w >= 2.0 and h >= 2.0:
						if not _overlaps_any_frame(Rect2(x, y, w, h)):
							frame_drawn.emit(Rect2(x, y, w, h))
					queue_redraw()
				else:
					# Click without drag on empty → blob click
					if _hover_rect.size.x > 0 and not _overlaps_any_frame(Rect2(_hover_rect.position, _hover_rect.size)):
						blob_clicked.emit(_hover_rect)


func _on_mouse_motion(mm: InputEventMouseMotion) -> void:
	if _panning:
		_pan = _pan_start_offset + mm.position - _pan_start
		queue_redraw()
		return

	if _resize_active:
		var delta_img := _s2i(mm.position) - _resize_mouse_start_img
		_resize_live = _resize_rect_from_drag(_resize_handle, delta_img)
		queue_redraw()
		return

	if _move_active:
		var delta_img := _s2i(mm.position) - _move_mouse_start_img
		var new_pos := _move_frame_orig.position + delta_img
		if _image_size.x > 0:
			new_pos.x = clampf(new_pos.x, 0.0, _image_size.x - _move_frame_orig.size.x)
			new_pos.y = clampf(new_pos.y, 0.0, _image_size.y - _move_frame_orig.size.y)
		if show_grid:
			@warning_ignore("integer_division")
			new_pos.x = float((int(new_pos.x) / GRID_TILE) * GRID_TILE)
			@warning_ignore("integer_division")
			new_pos.y = float((int(new_pos.y) / GRID_TILE) * GRID_TILE)
		_move_live_pos = new_pos
		queue_redraw()
		return

	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		var dist := mm.position.distance_to(_press_pos_screen)
		if dist > DRAG_THRESHOLD or _drawing:
			_drawing = true
			_draw_cur_img = _s2i(mm.position)
			queue_redraw()
		return

	# Hover: detectar blob bajo el cursor usando el mapa pre-computado
	if _blob_map.size() > 0 and _texture != null:
		var ip := _s2i(mm.position)
		var px := int(ip.x)
		var py := int(ip.y)
		var prev_rect := _hover_rect
		_hover_rect = Rect2i()

		# Skip hover detection if cursor is over an existing frame
		if _hit_test_frame(ip) >= 0:
			if _hover_rect != prev_rect:
				queue_redraw()
			return

		if detect_enabled and _content_regions.size() > 0:
			# Content region detection → snap to tile grid
			for region in _content_regions:
				var r: Rect2i = region
				if px >= r.position.x and px < r.position.x + r.size.x \
				and py >= r.position.y and py < r.position.y + r.size.y:
					_hover_rect = _snap_to_tile_grid(r)
					break
		elif detect_enabled and px >= 0 and px < _blob_map_w and py >= 0:
			# Blob detection fallback → snap to tile grid
			var map_h := _blob_map.size() / _blob_map_w
			if py < map_h:
				var blob_id := _blob_map[py * _blob_map_w + px]
				if blob_id > 0 and blob_id < _blob_id_to_rect.size():
					var rect_idx := _blob_id_to_rect[blob_id]
					if rect_idx >= 0 and rect_idx < _blob_rects.size():
						var raw_rect: Rect2i = _blob_rects[rect_idx]
						_hover_rect = _snap_to_tile_grid(raw_rect)

		# Suppress hover if it overlaps an existing frame
		if _hover_rect.size.x > 0 and _overlaps_any_frame(Rect2(_hover_rect.position, _hover_rect.size)):
			_hover_rect = Rect2i()

		if _hover_rect != prev_rect:
			queue_redraw()


func _hit_test_frame(img_pos: Vector2) -> int:
	for i in range(_frames.size() - 1, -1, -1):
		var fr: Dictionary = _frames[i]
		if img_pos.x >= fr.sx and img_pos.x < fr.sx + fr.w \
		and img_pos.y >= fr.sy and img_pos.y < fr.sy + fr.h:
			return i
	return -1



func _overlaps_any_frame(r: Rect2) -> bool:
	for i in range(_frames.size()):
		if i == _selected_frame:
			continue  # ignorar el frame que se está moviendo
		var fr: Dictionary = _frames[i]
		var fr_rect := Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
		if r.intersects(fr_rect):
			return true
	return false


func get_selected_frames() -> Array[int]:
	return _selected_frames


func clear_multi_select() -> void:
	_selected_frames.clear()
	multi_frame_selected.emit([])
	queue_redraw()


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED:
		queue_redraw()
	elif what == NOTIFICATION_MOUSE_ENTER:
		grab_focus()  # para recibir teclas (Esc, Delete)
