## FramePreviewPanel.gd — Preview de frames / animacion de sprite

class_name FramePreviewPanel
extends Control

var _texture: ImageTexture = null
var _frames: Array = []
var _current_idx: int = 0

const COL_BG    := Color(0.10, 0.10, 0.10)
const CHECKER_A := Color(0.38, 0.38, 0.38)
const CHECKER_B := Color(0.24, 0.24, 0.24)
const CS        := 8.0


func set_image(img: Image) -> void:
	if img != null:
		_texture = ImageTexture.create_from_image(img)
	else:
		_texture = null
	queue_redraw()


func set_frames(frames: Array) -> void:
	_frames = frames
	if _current_idx >= _frames.size():
		_current_idx = maxi(0, _frames.size() - 1)
	queue_redraw()


func show_frame(idx: int) -> void:
	if _frames.is_empty():
		return
	_current_idx = clampi(idx, 0, _frames.size() - 1)
	queue_redraw()


func get_frame_count() -> int:
	return _frames.size()


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), COL_BG)

	if _texture == null or _frames.is_empty():
		draw_string(ThemeDB.fallback_font, size * 0.5 - Vector2(55, 6),
			"Sin frames cargados", HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color(0.4, 0.4, 0.4))
		return

	var idx: int = clampi(_current_idx, 0, _frames.size() - 1)
	var f: Dictionary = _frames[idx]
	var fw: int = f.w
	var fh: int = f.h
	if fw <= 0 or fh <= 0:
		return

	var src_rect := Rect2(float(f.sx), float(f.sy), float(fw), float(fh))
	var scale: float = minf(size.x / float(fw), size.y / float(fh)) * 0.95
	var dw: float = fw * scale
	var dh: float = fh * scale
	var dx: float = (size.x - dw) * 0.5
	var dy: float = (size.y - dh) * 0.5
	var dst_rect := Rect2(dx, dy, dw, dh)

	# Checkerboard background
	var cols: int = ceili(dw / CS)
	var rows: int = ceili(dh / CS)
	for row in range(rows):
		for col in range(cols):
			var cc: Color = CHECKER_A if (col + row) % 2 == 0 else CHECKER_B
			draw_rect(Rect2(
				dx + col * CS, dy + row * CS,
				minf(CS, dw - col * CS), minf(CS, dh - row * CS)
			), cc)

	draw_texture_rect_region(_texture, dst_rect, src_rect)

	# Border around frame
	draw_rect(dst_rect, Color(0.5, 0.5, 0.5, 0.6), false, 1.0)

	# Info overlay
	var info := "Frame %d / %d   —   %d x %d px" % [idx + 1, _frames.size(), fw, fh]
	draw_rect(Rect2(0.0, size.y - 16.0, size.x, 16.0), Color(0, 0, 0, 0.55))
	draw_string(ThemeDB.fallback_font, Vector2(4.0, size.y - 3.0),
		info, HORIZONTAL_ALIGNMENT_LEFT, -1, 10, Color(1, 1, 1, 0.8))
