## SpriteCanvas.gd — Canvas interactivo con hover-detection de sprites

class_name SpriteCanvas
extends Control

signal frame_drawn(rect: Rect2)                     # Frame dibujado manualmente
signal frame_selected(index: int)                   # Frame existente clickeado (-1 = deselect)
signal blob_clicked(rect: Rect2i)                   # Blob hover clickeado para agregar
signal frame_resized(index: int, new_rect: Rect2)   # Frame redimensionado via handles
signal frame_delete_pressed(index: int)             # Tecla Delete sobre frame seleccionado
signal ao_candidate_clicked(rect: Rect2i)           # AO candidate frame clicked to add
signal ao_add_all_pressed(rects: Array)             # Add all AO candidates at once

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

# ── AO candidate frames (subdivision mode) ───────────────────────────────────
var _ao_candidates: Array = []           # Array of Rect2i — detected sub-frames
var _ao_hover_idx: int = -1              # Which candidate is hovered

# ── Frames definidos ──────────────────────────────────────────────────────────

var _frames: Array = []
var _selected_frame: int = -1

# ── Tool mode (0=Select, 1=Draw, 2=Pan) ─────────────────────────────────────

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

# ── Snap ─────────────────────────────────────────────────────────────────────
# snap_mode: 0=ninguno  1=multiplo  2=potencia-de-2 (c/dim)  3=cuadrado-pot2

var snap_mode: int = 0
var snap_x: int = 32
var snap_y: int = 32

# ── Visibility toggle ────────────────────────────────────────────────────────

var show_frames: bool = true

func set_snap(mode: int, sx: int, sy: int) -> void:
	snap_mode = mode
	snap_x = sx
	snap_y = sy
	_hover_rect = Rect2i()
	queue_redraw()

static func _next_pow2(v: int) -> int:
	if v <= 1: return 1
	var p := 1
	while p < v:
		p <<= 1
	return p

func _apply_snap(rect: Rect2i) -> Rect2i:
	var new_x: int = rect.position.x
	var new_y: int = rect.position.y
	var new_w: int = rect.size.x
	var new_h: int = rect.size.y
	@warning_ignore("integer_division")
	var cx: int = rect.position.x + rect.size.x / 2
	@warning_ignore("integer_division")
	var cy: int = rect.position.y + rect.size.y / 2

	match snap_mode:
		0:  # Sin snap
			pass
		1:  # Multiplo de snap_x / snap_y
			var sx: int = maxi(1, snap_x)
			var sy: int = maxi(1, snap_y)
			@warning_ignore("integer_division")
			new_w = ((rect.size.x + sx - 1) / sx) * sx
			@warning_ignore("integer_division")
			new_h = ((rect.size.y + sy - 1) / sy) * sy
			@warning_ignore("integer_division")
			new_x = cx - new_w / 2
			@warning_ignore("integer_division")
			new_y = cy - new_h / 2
		2:  # Potencia de 2 por dimension
			new_w = _next_pow2(rect.size.x)
			new_h = _next_pow2(rect.size.y)
			@warning_ignore("integer_division")
			new_x = cx - new_w / 2
			@warning_ignore("integer_division")
			new_y = cy - new_h / 2
		3:  # Cuadrado potencia de 2 (usa la mayor dimension)
			var dim: int = _next_pow2(maxi(rect.size.x, rect.size.y))
			new_w = dim
			new_h = dim
			@warning_ignore("integer_division")
			new_x = cx - dim / 2
			@warning_ignore("integer_division")
			new_y = cy - dim / 2
		5:  # AO Tiles: offset ×8, size ×8
			# Snap position DOWN to nearest 8px
			@warning_ignore("integer_division")
			new_x = (rect.position.x / 8) * 8
			@warning_ignore("integer_division")
			new_y = (rect.position.y / 8) * 8
			# Right/bottom edge of original content
			var right_edge: int = rect.position.x + rect.size.x
			var bottom_edge: int = rect.position.y + rect.size.y
			# Size = ceil to next multiple of 8 so all content fits
			@warning_ignore("integer_division")
			new_w = maxi(8, ((right_edge - new_x + 7) / 8) * 8)
			@warning_ignore("integer_division")
			new_h = maxi(8, ((bottom_edge - new_y + 7) / 8) * 8)

	# Always clamp to image borders regardless of snap mode
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
	_drawing = false
	_hover_rect = Rect2i()
	_blob_map = PackedInt32Array()
	_blob_rects = []
	_content_regions = []
	_ao_candidates = []
	_ao_hover_idx = -1
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

	# AO 32px grid overlay
	if snap_mode == 5 and _zoom >= 0.15:
		_draw_ao_grid(img_sr)

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
			# Durante resize/move activo, usar el rect live para el frame seleccionado
			var draw_r: Rect2
			if is_sel and _resize_active:
				draw_r = _resize_live
			elif is_sel and _move_active:
				draw_r = Rect2(_move_live_pos, _move_frame_orig.size)
			else:
				draw_r = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
			var sr := _irect2srect(draw_r)
			draw_rect(sr, Color(col.r, col.g, col.b, 0.15 if is_sel else 0.06))
			draw_rect(sr, Color.WHITE if is_sel else col, false, 2.0 if is_sel else 1.0)
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
		# Show snapped preview while drawing
		var ix := minf(_draw_start_img.x, _draw_cur_img.x)
		var iy := minf(_draw_start_img.y, _draw_cur_img.y)
		var iw := absf(_draw_cur_img.x - _draw_start_img.x)
		var ih := absf(_draw_cur_img.y - _draw_start_img.y)
		if snap_mode != 0 and iw >= 2.0 and ih >= 2.0:
			var snapped := _apply_snap(Rect2i(int(ix), int(iy), int(iw), int(ih)))
			var sr := _irect2srect(Rect2(snapped.position, snapped.size))
			draw_rect(sr, Color(0.3, 1.0, 0.3, 0.15))
			draw_rect(sr, Color(0.3, 1.0, 0.3, 0.9), false, 2.0)
			# Size label
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

	# AO candidate frames
	if _ao_candidates.size() > 0:
		for i in range(_ao_candidates.size()):
			var cr: Rect2i = _ao_candidates[i]
			var sr := _irect2srect(Rect2(cr.position, cr.size))
			var is_hovered := (i == _ao_hover_idx)
			var col_fill := Color(0.0, 1.0, 0.5, 0.20) if is_hovered else Color(0.0, 0.8, 1.0, 0.10)
			var col_border := Color(0.0, 1.0, 0.5, 0.95) if is_hovered else Color(0.0, 0.8, 1.0, 0.70)
			draw_rect(sr, col_fill)
			draw_rect(sr, col_border, false, 2.0 if is_hovered else 1.0)
			if sr.size.x > 30:
				var lbl := "%dx%d" % [cr.size.x, cr.size.y]
				if is_hovered:
					lbl = "Click: %d,%d %dx%d" % [cr.position.x, cr.position.y, cr.size.x, cr.size.y]
				draw_string(ThemeDB.fallback_font, sr.position + Vector2(3, 14),
					lbl, HORIZONTAL_ALIGNMENT_LEFT, -1, 11,
					Color(0.0, 1.0, 0.5, 0.95) if is_hovered else Color(0.0, 0.8, 1.0, 0.85))
		# Show count + hint at top
		var hint := "%d candidatos AO  |  Click=agregar  |  Enter=agregar todos  |  Esc=cancelar" % _ao_candidates.size()
		draw_string(ThemeDB.fallback_font, Vector2(6, 16), hint,
			HORIZONTAL_ALIGNMENT_LEFT, -1, 12, Color(0.0, 1.0, 0.7, 0.9))

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


func _draw_ao_grid(img_sr: Rect2) -> void:
	var grid_step := 32.0 * _zoom
	# Skip drawing if grid lines would be too dense (< 4px apart)
	if grid_step < 4.0:
		return
	# Determine visible area
	var vis_left := maxf(img_sr.position.x, 0.0)
	var vis_top := maxf(img_sr.position.y, 0.0)
	var vis_right := minf(img_sr.position.x + img_sr.size.x, size.x)
	var vis_bottom := minf(img_sr.position.y + img_sr.size.y, size.y)
	# Thin lines for 32px grid
	var col_grid := Color(1.0, 0.85, 0.0, 0.12)
	# Thicker lines every 128px (4 tiles)
	var col_grid_major := Color(1.0, 0.85, 0.0, 0.28)
	# Vertical lines
	@warning_ignore("integer_division")
	var first_col := int((vis_left - img_sr.position.x) / grid_step)
	var total_cols := ceili(_image_size.x / 32.0)
	for i in range(first_col, total_cols + 1):
		var sx := img_sr.position.x + float(i) * grid_step
		if sx < vis_left or sx > vis_right:
			continue
		var is_major := (i % 4 == 0)
		draw_line(Vector2(sx, vis_top), Vector2(sx, vis_bottom),
			col_grid_major if is_major else col_grid,
			2.0 if is_major else 1.0)
	# Horizontal lines
	@warning_ignore("integer_division")
	var first_row := int((vis_top - img_sr.position.y) / grid_step)
	var total_rows := ceili(_image_size.y / 32.0)
	for i in range(first_row, total_rows + 1):
		var sy := img_sr.position.y + float(i) * grid_step
		if sy < vis_top or sy > vis_bottom:
			continue
		var is_major := (i % 4 == 0)
		draw_line(Vector2(vis_left, sy), Vector2(vis_right, sy),
			col_grid_major if is_major else col_grid,
			2.0 if is_major else 1.0)


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
			if _ao_candidates.size() > 0:
				ao_clear()
			elif _selected_frame >= 0:
				_selected_frame = -1
				frame_selected.emit(-1)
				queue_redraw()
		KEY_ENTER, KEY_KP_ENTER:
			if _ao_candidates.size() > 0:
				ao_add_all()
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
				queue_redraw()
		MOUSE_BUTTON_WHEEL_DOWN:
			if mb.pressed:
				var old := _zoom
				_zoom = maxf(_zoom / 1.15, 0.02)
				_pan = mb.position - (mb.position - _pan) * (_zoom / old)
				queue_redraw()
		MOUSE_BUTTON_MIDDLE, MOUSE_BUTTON_RIGHT:
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
					if tool_mode == 2:
						# PAN mode: left-click pans
						_panning = true
						_pan_start = mb.position
						_pan_start_offset = _pan
					elif tool_mode == 0:
						# SELECT mode: resize handles, frame select/move
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
								if hit == _selected_frame:
									var fr: Dictionary = _frames[_selected_frame]
									_move_frame_orig = Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
									_move_mouse_start_img = ip
									_move_live_pos = _move_frame_orig.position
									_move_active = true
								else:
									_selected_frame = hit
									frame_selected.emit(hit)
									queue_redraw()
							else:
								if _selected_frame >= 0:
									_selected_frame = -1
									frame_selected.emit(-1)
									queue_redraw()
					else:
						# DRAW mode: draw new frame or click blob
						if _selected_frame >= 0:
							_selected_frame = -1
							frame_selected.emit(-1)
							queue_redraw()
						if snap_mode == 5:
							@warning_ignore("integer_division")
							ip.x = float((int(ip.x) / 8) * 8)
							@warning_ignore("integer_division")
							ip.y = float((int(ip.y) / 8) * 8)
						_draw_start_img = ip
						_draw_cur_img = ip
			else:
				if _panning and tool_mode == 2:
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
					if snap_mode == 5:
						@warning_ignore("integer_division")
						new_pos.x = float((int(new_pos.x) / 8) * 8)
						@warning_ignore("integer_division")
						new_pos.y = float((int(new_pos.y) / 8) * 8)
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
					# Apply snap (pow2, grid, etc.) to drawn frame
					if snap_mode != 0 and w >= 2.0 and h >= 2.0:
						var snapped := _apply_snap(Rect2i(int(x), int(y), int(w), int(h)))
						x = float(snapped.position.x)
						y = float(snapped.position.y)
						w = float(snapped.size.x)
						h = float(snapped.size.y)
					if w >= 2.0 and h >= 2.0:
						if snap_mode == 5:
							# AO mode: subdivide drawn area into individual blob-based frames
							_ao_subdivide(Rect2i(int(x), int(y), int(w), int(h)))
						elif not _overlaps_any_frame(Rect2(x, y, w, h)):
							frame_drawn.emit(Rect2(x, y, w, h))
					queue_redraw()
				else:
					# Click without drag in DRAW mode
					if snap_mode == 5 and _ao_candidates.size() > 0:
						# AO mode: click on a candidate to add it
						var ip := _s2i(mb.position)
						var clicked_idx := _ao_hit_test(ip)
						if clicked_idx >= 0:
							ao_candidate_clicked.emit(_ao_candidates[clicked_idx])
							_ao_candidates.remove_at(clicked_idx)
							_ao_hover_idx = -1
							queue_redraw()
					elif _hover_rect.size.x > 0 and not _overlaps_any_frame(Rect2(_hover_rect.position, _hover_rect.size)):
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
		if snap_mode == 5:
			@warning_ignore("integer_division")
			new_pos.x = float((int(new_pos.x) / 32) * 32)
			@warning_ignore("integer_division")
			new_pos.y = float((int(new_pos.y) / 32) * 32)
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

	# AO candidates hover
	if _ao_candidates.size() > 0:
		var ip := _s2i(mm.position)
		var prev_idx := _ao_hover_idx
		_ao_hover_idx = _ao_hit_test(ip)
		if _ao_hover_idx != prev_idx:
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

		if snap_mode == 4 and _content_regions.size() > 0:
			# Smart mode: find which pre-computed content region the cursor is in
			for region in _content_regions:
				var r: Rect2i = region
				if px >= r.position.x and px < r.position.x + r.size.x \
				and py >= r.position.y and py < r.position.y + r.size.y:
					_hover_rect = r
					break
		elif snap_mode == 5 and _blob_rects.size() > 0:
			# AO mode: find which 8px-aligned cell the cursor is in,
			# then merge all blobs that overlap that cell into one frame
			@warning_ignore("integer_division")
			var cell_x: int = (px / 8) * 8
			@warning_ignore("integer_division")
			var cell_y: int = (py / 8) * 8
			# Start with a minimal cell, grow to include all intersecting blobs
			var merged := Rect2i(cell_x, cell_y, 8, 8)
			var found_any := false
			var changed := true
			while changed:
				changed = false
				for blob_rect in _blob_rects:
					var br: Rect2i = blob_rect
					if _rects_overlap(merged, br):
						# Expand merged to contain the blob, then re-snap
						var union_x := mini(merged.position.x, br.position.x)
						var union_y := mini(merged.position.y, br.position.y)
						var union_r := maxi(merged.position.x + merged.size.x, br.position.x + br.size.x)
						var union_b := maxi(merged.position.y + merged.size.y, br.position.y + br.size.y)
						var snapped := _apply_snap(Rect2i(union_x, union_y, union_r - union_x, union_b - union_y))
						if snapped != merged:
							merged = snapped
							changed = true
						found_any = true
			if found_any:
				_hover_rect = merged
		elif px >= 0 and px < _blob_map_w and py >= 0:
			var map_h := _blob_map.size() / _blob_map_w
			if py < map_h:
				var blob_id := _blob_map[py * _blob_map_w + px]
				if blob_id > 0 and blob_id < _blob_id_to_rect.size():
					var rect_idx := _blob_id_to_rect[blob_id]
					if rect_idx >= 0 and rect_idx < _blob_rects.size():
						var raw_rect: Rect2i = _blob_rects[rect_idx]
						_hover_rect = _apply_snap(raw_rect)

		# Suppress hover if it overlaps an existing frame
		if _hover_rect.size.x > 0 and _overlaps_any_frame(Rect2(_hover_rect.position, _hover_rect.size)):
			_hover_rect = Rect2i()

		if _hover_rect != prev_rect:
			queue_redraw()


# ── AO subdivision ───────────────────────────────────────────────────────────

func _ao_subdivide(area: Rect2i) -> void:
	# Find all blobs that overlap the drawn area, snap each to ×8
	_ao_candidates.clear()
	_ao_hover_idx = -1
	var used_blobs: Array = []  # track which blobs are already merged
	for i in range(_blob_rects.size()):
		var br: Rect2i = _blob_rects[i]
		if not _rects_overlap(area, br):
			continue
		# Check if this blob is already part of an existing candidate (merged)
		var already := false
		for u in used_blobs:
			if u == i:
				already = true
				break
		if already:
			continue
		# Start with this blob snapped
		var snapped := _apply_snap(br)
		# Iteratively merge nearby blobs that overlap the snapped rect
		var changed := true
		while changed:
			changed = false
			for j in range(_blob_rects.size()):
				var br2: Rect2i = _blob_rects[j]
				if not _rects_overlap(area, br2):
					continue
				if not _rects_overlap(snapped, br2):
					continue
				var union_x := mini(snapped.position.x, br2.position.x)
				var union_y := mini(snapped.position.y, br2.position.y)
				var union_r := maxi(snapped.position.x + snapped.size.x, br2.position.x + br2.size.x)
				var union_b := maxi(snapped.position.y + snapped.size.y, br2.position.y + br2.size.y)
				var new_snapped := _apply_snap(Rect2i(union_x, union_y, union_r - union_x, union_b - union_y))
				if new_snapped != snapped:
					snapped = new_snapped
					changed = true
				# Mark blob as used
				var found_j := false
				for u in used_blobs:
					if u == j:
						found_j = true
						break
				if not found_j:
					used_blobs.append(j)
		# Skip if overlaps existing frames
		if not _overlaps_any_frame(Rect2(snapped.position, snapped.size)):
			# Skip duplicates
			var dupe := false
			for c in _ao_candidates:
				if c == snapped:
					dupe = true
					break
			if not dupe:
				_ao_candidates.append(snapped)
	queue_redraw()


func _ao_hit_test(img_pos: Vector2) -> int:
	for i in range(_ao_candidates.size() - 1, -1, -1):
		var r: Rect2i = _ao_candidates[i]
		if img_pos.x >= r.position.x and img_pos.x < r.position.x + r.size.x \
		and img_pos.y >= r.position.y and img_pos.y < r.position.y + r.size.y:
			return i
	return -1


func ao_add_all() -> void:
	if _ao_candidates.size() > 0:
		ao_add_all_pressed.emit(_ao_candidates.duplicate())
		_ao_candidates.clear()
		_ao_hover_idx = -1
		queue_redraw()


func ao_clear() -> void:
	_ao_candidates.clear()
	_ao_hover_idx = -1
	queue_redraw()


func _hit_test_frame(img_pos: Vector2) -> int:
	for i in range(_frames.size() - 1, -1, -1):
		var fr: Dictionary = _frames[i]
		if img_pos.x >= fr.sx and img_pos.x < fr.sx + fr.w \
		and img_pos.y >= fr.sy and img_pos.y < fr.sy + fr.h:
			return i
	return -1


static func _rects_overlap(a: Rect2i, b: Rect2i) -> bool:
	return a.position.x < b.position.x + b.size.x \
		and a.position.x + a.size.x > b.position.x \
		and a.position.y < b.position.y + b.size.y \
		and a.position.y + a.size.y > b.position.y


func _overlaps_any_frame(r: Rect2) -> bool:
	for i in range(_frames.size()):
		if i == _selected_frame:
			continue  # ignorar el frame que se está moviendo
		var fr: Dictionary = _frames[i]
		var fr_rect := Rect2(float(fr.sx), float(fr.sy), float(fr.w), float(fr.h))
		if r.intersects(fr_rect):
			return true
	return false


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED:
		queue_redraw()
	elif what == NOTIFICATION_MOUSE_ENTER:
		grab_focus()  # para recibir teclas (Esc, Delete)
