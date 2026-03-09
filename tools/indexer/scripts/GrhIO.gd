## GrhIO.gd — Lector/escritor binario de Graficos.ind
## Formato: Version(Int32) + Count(Int32) + entradas
## Cada entrada: GrhIndex(Int32) + NumFrames(Int16) + datos

class_name GrhIO
extends RefCounted

const MI_CABECERA_SIZE := 263  # 255 desc + 4 crc + 4 magic

# Retorna Dictionary con:
#   version: int
#   max_index: int
#   entries: Dictionary { grh_index(int) -> entry(Dictionary) }
# Cada entry estático: { grh_index, num_frames=1, file_num, sx, sy, width, height }
# Cada entry animado:  { grh_index, num_frames, frame_indices: Array[int], speed: float }
static func load_ind(path: String) -> Dictionary:
	var result := {
		"version": 12,
		"max_index": 0,
		"entries": {}
	}

	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		push_error("GrhIO: no se pudo abrir " + path)
		return result

	# Auto-detectar cabecera
	var version := _read_int32(f)
	var grh_count := _read_int32(f)

	if grh_count <= 0 or grh_count > 100000:
		f.seek(MI_CABECERA_SIZE)
		version = _read_int32(f)
		grh_count = _read_int32(f)

	if grh_count <= 0 or grh_count > 100000:
		f.seek(0)
		f.get_8()  # saltar 1 byte de flag
		version = _read_int32(f)
		grh_count = _read_int32(f)

	result["version"] = version

	while f.get_position() + 6 <= f.get_length():
		var grh_index := _read_int32(f)
		if grh_index <= 0:
			break

		var num_frames := _read_int16(f)
		var entry := {"grh_index": grh_index, "num_frames": num_frames}

		if num_frames > 1:
			var frame_indices: Array[int] = []
			for i in range(num_frames):
				frame_indices.append(_read_int32(f))
			entry["frame_indices"] = frame_indices
			entry["speed"] = f.get_float()
		else:
			entry["file_num"] = _read_int32(f)
			entry["sx"]       = _read_int16(f)
			entry["sy"]       = _read_int16(f)
			entry["width"]    = _read_int16(f)
			entry["height"]   = _read_int16(f)

		result["entries"][grh_index] = entry
		if grh_index > result["max_index"]:
			result["max_index"] = grh_index

	f.close()
	print("GrhIO: cargadas %d entradas (max_index=%d, version=%d)" % [
		result["entries"].size(), result["max_index"], version])
	return result


static func save_ind(path: String, data: Dictionary) -> bool:
	var f := FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		push_error("GrhIO: no se pudo escribir " + path)
		return false

	var entries: Dictionary = data.get("entries", {})
	var version: int = data.get("version", 12)

	# Calcular max_index
	var max_index := 0
	for key in entries:
		if key > max_index:
			max_index = key

	_write_int32(f, version)
	_write_int32(f, max_index)

	# Escribir en orden
	var sorted_keys := entries.keys()
	sorted_keys.sort()

	for key in sorted_keys:
		var entry: Dictionary = entries[key]
		_write_int32(f, entry["grh_index"])

		if entry.get("num_frames", 1) > 1:
			_write_int16(f, entry["num_frames"])
			for fi in entry["frame_indices"]:
				_write_int32(f, fi)
			f.store_float(entry["speed"])
		else:
			_write_int16(f, 1)
			_write_int32(f, entry["file_num"])
			_write_int16(f, entry["sx"])
			_write_int16(f, entry["sy"])
			_write_int16(f, entry["width"])
			_write_int16(f, entry["height"])

	f.close()
	print("GrhIO: guardadas %d entradas en %s" % [sorted_keys.size(), path])
	return true


static func to_text(data: Dictionary) -> String:
	var lines: PackedStringArray = PackedStringArray()
	lines.append("# Graficos.ind  version=%d  max_index=%d" % [data.get("version", 12), data.get("max_index", 0)])
	lines.append("# static: GrhIndex 1 FileNum SX SY W H")
	lines.append("# anim:   GrhIndex NumFrames F1 F2...Fn Speed")
	var sorted_keys: Array = data["entries"].keys()
	sorted_keys.sort()
	for key in sorted_keys:
		var e: Dictionary = data["entries"][key]
		if e.get("num_frames", 1) > 1:
			var parts: Array = [str(e.grh_index), str(e.num_frames)]
			for fi in e.get("frame_indices", []):
				parts.append(str(fi))
			parts.append("%.4f" % e.get("speed", 0.0))
			lines.append(" ".join(parts))
		else:
			lines.append("%d 1 %d %d %d %d %d" % [
				e.grh_index, e.get("file_num", 0),
				e.get("sx", 0), e.get("sy", 0),
				e.get("width", 0), e.get("height", 0)])
	return "\n".join(lines)


static func from_text(text: String) -> Dictionary:
	var result := {"version": 12, "max_index": 0, "entries": {}}
	for raw_line in text.split("\n"):
		var line := raw_line.strip_edges()
		if line.is_empty() or line.begins_with("#"):
			continue
		var parts := line.split(" ")
		if parts.size() < 2:
			continue
		var grh_index: int = parts[0].to_int()
		var num_frames: int = parts[1].to_int()
		if grh_index <= 0:
			continue
		var entry := {"grh_index": grh_index, "num_frames": num_frames}
		if num_frames == 1 and parts.size() >= 7:
			entry["file_num"] = parts[2].to_int()
			entry["sx"]       = parts[3].to_int()
			entry["sy"]       = parts[4].to_int()
			entry["width"]    = parts[5].to_int()
			entry["height"]   = parts[6].to_int()
		elif num_frames > 1 and parts.size() >= 2 + num_frames + 1:
			var frame_indices: Array[int] = []
			for i in range(num_frames):
				frame_indices.append(parts[2 + i].to_int())
			entry["frame_indices"] = frame_indices
			entry["speed"] = parts[2 + num_frames].to_float()
		else:
			continue
		result["entries"][grh_index] = entry
		if grh_index > result["max_index"]:
			result["max_index"] = grh_index
	return result


# ── Helpers de lectura/escritura con signo ──────────────────────────────────

static func _read_int32(f: FileAccess) -> int:
	var val := f.get_32()
	# Convertir uint32 a int32 (signo)
	if val >= 0x80000000:
		return val - 0x100000000
	return val

static func _read_int16(f: FileAccess) -> int:
	var val := f.get_16()
	# Convertir uint16 a int16 (signo)
	if val >= 0x8000:
		return val - 0x10000
	return val

static func _write_int32(f: FileAccess, val: int) -> void:
	f.store_32(val & 0xFFFFFFFF)

static func _write_int16(f: FileAccess, val: int) -> void:
	f.store_16(val & 0xFFFF)
