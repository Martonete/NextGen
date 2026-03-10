## FileListPanel.gd — Left panel: file browser with thumbnails and search
class_name FileListPanel
extends VBoxContainer

signal file_selected(path: String, file_num: int)

var _file_list: ItemList
var _search_edit: LineEdit
var _lbl_count: Label

var _all_files: Array[String] = []
var _filtered_indices: Array[int] = []  # indices into _all_files matching filter
var _thumb_queue: Array[int] = []       # indices into _all_files pending thumb load
var _loaded_thumbs: Dictionary = {}     # path -> bool

# External: set by Main when using client mode
var _using_client: bool = false
var _filenum_base: int = 1


func _ready() -> void:
	add_theme_constant_override("separation", 2)
	custom_minimum_size.x = 220

	# Header
	var header := IndexerTheme.heading("Graficos")
	add_child(header)

	# Search
	_search_edit = LineEdit.new()
	_search_edit.placeholder_text = "Buscar archivo..."
	_search_edit.clear_button_enabled = true
	_search_edit.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	_search_edit.text_changed.connect(_on_search_changed)
	add_child(_search_edit)

	# File list
	_file_list = ItemList.new()
	_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_file_list.icon_mode = ItemList.ICON_MODE_LEFT
	_file_list.fixed_icon_size = Vector2i(48, 48)
	_file_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_MD)
	_file_list.item_selected.connect(_on_item_selected)
	add_child(_file_list)

	# Count label
	_lbl_count = IndexerTheme.label("", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	add_child(_lbl_count)


func load_folder(path: String) -> void:
	_all_files.clear()
	_filtered_indices.clear()
	_file_list.clear()
	_loaded_thumbs.clear()
	_thumb_queue.clear()

	var dir := DirAccess.open(path)
	if dir == null:
		return

	dir.list_dir_begin()
	var fname := dir.get_next()
	while fname != "":
		var ext := fname.get_extension().to_lower()
		if ext in ["png", "jpg", "jpeg", "bmp", "tga", "webp"]:
			# Only include files with purely numeric basenames (e.g. "041.png", "1.bmp")
			# Skip interface/UI graphics that have text names (e.g. "VentanaPrincipal.jpg")
			var basename := fname.get_basename()
			if basename.is_valid_int():
				_all_files.append(path.path_join(fname))
		fname = dir.get_next()
	dir.list_dir_end()

	_all_files.sort_custom(func(a, b): return _sort_num(a) < _sort_num(b))
	_apply_filter()


func process_thumbnails(count: int = 5) -> void:
	var processed := 0
	while not _thumb_queue.is_empty() and processed < count:
		var file_idx: int = _thumb_queue.pop_front()
		_load_thumb(file_idx)
		processed += 1


func get_file_count() -> int:
	return _all_files.size()


func get_file_path(idx: int) -> String:
	if idx >= 0 and idx < _all_files.size():
		return _all_files[idx]
	return ""


func select_by_file_num(fnum: int) -> void:
	for i in range(_all_files.size()):
		var path := _all_files[i]
		if get_file_num(path) == fnum:
			# Find in filtered list
			for j in range(_filtered_indices.size()):
				if _filtered_indices[j] == i:
					_file_list.select(j)
					_file_list.ensure_current_is_visible()
					file_selected.emit(path, fnum)
					return
			# Not in current filter — clear filter first
			_search_edit.text = ""
			_apply_filter()
			for j in range(_filtered_indices.size()):
				if _filtered_indices[j] == i:
					_file_list.select(j)
					_file_list.ensure_current_is_visible()
					file_selected.emit(path, fnum)
					return
			return


## Select and scroll to a file number without emitting file_selected.
func scroll_to_file_num(fnum: int) -> void:
	for i in range(_all_files.size()):
		if get_file_num(_all_files[i]) == fnum:
			for j in range(_filtered_indices.size()):
				if _filtered_indices[j] == i:
					_file_list.select(j)
					_file_list.ensure_current_is_visible()
					return
			_search_edit.text = ""
			_apply_filter()
			for j in range(_filtered_indices.size()):
				if _filtered_indices[j] == i:
					_file_list.select(j)
					_file_list.ensure_current_is_visible()
					return
			return


func get_file_num(path: String) -> int:
	if _using_client:
		var basename := path.get_file().get_basename()
		return basename.to_int() if basename.is_valid_int() else 0
	var fname := path.get_file().get_basename()
	var digits := fname.lstrip("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_- ")
	if digits.is_valid_int():
		return _filenum_base + digits.to_int()
	return _filenum_base + _all_files.find(path)


# ── Internal ──────────────────────────────────────────────────────

func _apply_filter() -> void:
	var query := _search_edit.text.strip_edges().to_lower()
	_filtered_indices.clear()
	_file_list.clear()
	_thumb_queue.clear()

	for i in range(_all_files.size()):
		var fname := _all_files[i].get_file()
		if query.is_empty() or fname.to_lower().contains(query):
			_filtered_indices.append(i)
			_file_list.add_item(fname)
			_thumb_queue.append(i)

	_lbl_count.text = "%d / %d archivos" % [_filtered_indices.size(), _all_files.size()]


func _on_search_changed(_text: String) -> void:
	_apply_filter()


func _on_item_selected(list_idx: int) -> void:
	if list_idx < 0 or list_idx >= _filtered_indices.size():
		return
	var file_idx := _filtered_indices[list_idx]
	var path := _all_files[file_idx]
	var fnum := get_file_num(path)
	file_selected.emit(path, fnum)


func _load_thumb(file_idx: int) -> void:
	if file_idx >= _all_files.size():
		return
	var path := _all_files[file_idx]
	if _loaded_thumbs.has(path):
		return

	# Find this file in the filtered list to get the ItemList index
	var list_idx := -1
	for i in range(_filtered_indices.size()):
		if _filtered_indices[i] == file_idx:
			list_idx = i
			break
	if list_idx < 0 or list_idx >= _file_list.item_count:
		return

	var bytes := FileAccess.get_file_as_bytes(path)
	if bytes.is_empty():
		return

	var img := Image.new()
	var ext := path.get_extension().to_lower()
	var err: Error

	if ext == "png":
		err = img.load_png_from_buffer(bytes)
		if err != OK:
			var clean := _strip_png_meta(bytes)
			err = img.load_png_from_buffer(clean)
	elif ext in ["jpg", "jpeg"]:
		err = img.load_jpg_from_buffer(bytes)
	elif ext == "bmp":
		err = img.load_bmp_from_buffer(bytes)
	else:
		err = img.load_png_from_buffer(bytes)

	if err != OK:
		return

	var scale := minf(48.0 / img.get_width(), 48.0 / img.get_height())
	img.resize(maxi(1, int(img.get_width() * scale)), maxi(1, int(img.get_height() * scale)), Image.INTERPOLATE_BILINEAR)
	_file_list.set_item_icon(list_idx, ImageTexture.create_from_image(img))
	_loaded_thumbs[path] = true


static func _sort_num(path: String) -> int:
	var fname := path.get_file().get_basename()
	var digits := fname.lstrip("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_- ")
	return digits.to_int() if digits.is_valid_int() else -1


static func _strip_png_meta(bytes: PackedByteArray) -> PackedByteArray:
	if bytes.size() < 8:
		return bytes
	var sig := [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
	for i in range(8):
		if bytes[i] != sig[i]:
			return bytes
	var skip := ["iCCP", "sRGB", "gAMA", "cHRM", "tEXt", "iTXt", "zTXt"]
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
		if t not in skip:
			result.append_array(bytes.slice(pos, chunk_end))
		pos = chunk_end
		if t == "IEND":
			break
	return result
