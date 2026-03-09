## FrameDetector.gd — Detección automática de frames en sprite sheets

class_name FrameDetector
extends RefCounted


# ── Detección indexada (pre-computa mapa pixel→blob para hover instantáneo) ──

# Retorna Dictionary:
#   "map":    PackedInt32Array  (tamaño w*h)  — 0=vacío, N=blob_id (1-based)
#   "rects":  Array[Rect2i]                   — rects[blob_id - 1] = Rect2i del blob
#   "width":  int
#   "height": int
static func detect_blobs_indexed(
	image: Image,
	alpha_threshold: float = 0.03,
	min_size: int = 3,
	padding: int = 1
) -> Dictionary:
	# Convertir a RGBA8 para acceso raw (4 bytes/pixel)
	var img := image.duplicate()
	img.convert(Image.FORMAT_RGBA8)
	var data: PackedByteArray = img.get_data()
	var w: int = img.get_width()
	var h: int = img.get_height()
	var n    := w * h
	var alpha_min := int(alpha_threshold * 255)

	var visited := PackedInt32Array()
	visited.resize(n)
	visited.fill(0)

	var blob_map := PackedInt32Array()
	blob_map.resize(n)
	blob_map.fill(0)

	# Acumularemos rects temporales indexadas por blob_id
	var blob_rects_raw: Array = []   # [ {min_x, max_x, min_y, max_y} ]
	var next_id := 1

	for i in range(n):
		if visited[i] != 0:
			continue
		var b := i * 4
		var a := data[b + 3]
		if _px_empty(data, b, a, alpha_min):
			visited[i] = -1
			continue

		# BFS con stack de enteros (más rápido que Vector2i)
		var stack: Array[int] = [i]
		visited[i] = next_id
		blob_map[i] = next_id
		var min_x := i % w
		var max_x := min_x
		var min_y := i / w
		var max_y := min_y

		while stack.size() > 0:
			var cur: int = stack.pop_back()
			var cx := cur % w
			var cy := cur / w
			if cx < min_x: min_x = cx
			if cx > max_x: max_x = cx
			if cy < min_y: min_y = cy
			if cy > max_y: max_y = cy

			# 4 vecinos usando índices enteros
			if cx > 0:     _try_add(cur - 1, data, visited, blob_map, stack, next_id, alpha_min)
			if cx < w - 1: _try_add(cur + 1, data, visited, blob_map, stack, next_id, alpha_min)
			if cy > 0:     _try_add(cur - w, data, visited, blob_map, stack, next_id, alpha_min)
			if cy < h - 1: _try_add(cur + w, data, visited, blob_map, stack, next_id, alpha_min)

		blob_rects_raw.append({"min_x": min_x, "max_x": max_x, "min_y": min_y, "max_y": max_y})
		next_id += 1

	# Construir array final de Rect2i — filtrar blobs demasiado pequeños
	# (mantener blob_id en el mapa pero no agregar el rect → quedan fuera del hover)
	var rects: Array = []
	var id_to_rect_idx := PackedInt32Array()
	id_to_rect_idx.resize(next_id)
	id_to_rect_idx.fill(-1)

	for blob_id in range(1, next_id):
		var r: Dictionary = blob_rects_raw[blob_id - 1]
		var fw: int = r.max_x - r.min_x + 1
		var fh: int = r.max_y - r.min_y + 1
		if fw >= min_size and fh >= min_size:
			var sx := maxi(0, r.min_x - padding)
			var sy := maxi(0, r.min_y - padding)
			var ex := mini(w - 1, r.max_x + padding)
			var ey := mini(h - 1, r.max_y + padding)
			id_to_rect_idx[blob_id] = rects.size()
			rects.append(Rect2i(sx, sy, ex - sx + 1, ey - sy + 1))

	return {
		"map": blob_map,
		"rects": rects,
		"id_to_rect": id_to_rect_idx,
		"width": w,
		"height": h
	}


# ── Detección por cuadrícula (sin cambios) ───────────────────────────────────

static func detect_grid(
	image: Image,
	cell_w: int, cell_h: int,
	off_x: int, off_y: int,
	margin_x: int, margin_y: int,
	skip_empty: bool
) -> Array:
	var frames: Array = []
	var img_w := image.get_width()
	var img_h := image.get_height()
	# Minimum fraction of cell size to still include a partial edge cell
	const PARTIAL_MIN := 0.70
	var min_cw: int = int(cell_w * PARTIAL_MIN)
	var min_ch: int = int(cell_h * PARTIAL_MIN)
	var row_y := off_y
	while row_y < img_h:
		var actual_h: int = mini(cell_h, img_h - row_y)
		if actual_h < min_ch:
			break
		var col_x := off_x
		while col_x < img_w:
			var actual_w: int = mini(cell_w, img_w - col_x)
			if actual_w < min_cw:
				break
			if not skip_empty or not _is_empty_sample(image, col_x, row_y, actual_w, actual_h):
				frames.append({ "sx": col_x, "sy": row_y, "w": actual_w, "h": actual_h })
			col_x += cell_w + margin_x
		row_y += cell_h + margin_y
	return frames


# ── Helpers internos ─────────────────────────────────────────────────────────

static func _try_add(
	ni: int, data: PackedByteArray,
	visited: PackedInt32Array, blob_map: PackedInt32Array,
	stack: Array, blob_id: int, alpha_min: int
) -> void:
	if visited[ni] != 0:
		return
	var b := ni * 4
	var a := data[b + 3]
	if _px_empty(data, b, a, alpha_min):
		visited[ni] = -1
	else:
		visited[ni] = blob_id
		blob_map[ni] = blob_id
		stack.append(ni)


static func _px_empty(data: PackedByteArray, b: int, a: int, alpha_min: int) -> bool:
	if a <= alpha_min:
		return true
	# Color-key AO: negro puro = transparente
	if data[b] <= 4 and data[b + 1] <= 4 and data[b + 2] <= 4:
		return true
	return false


static func _is_empty_sample(image: Image, x: int, y: int, w: int, h: int) -> bool:
	var step := maxi(1, mini(w, h) / 6)
	var py := y
	while py < y + h:
		var px := x
		while px < x + w:
			var c := image.get_pixel(px, py)
			if c.a > 0.03 and not (c.r <= 0.015 and c.g <= 0.015 and c.b <= 0.015):
				return false
			px += step
		py += step
	return true
