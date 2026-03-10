## AssetBrowser.gd — Visual browser/editor for INIT asset data (in-memory)
## All changes modify in-memory arrays only. Disk writes happen via the global GUARDAR button.
## Supports: GRHs, Bodies, Heads, Helmets, Weapons, Shields, FXs + raw text fallback.

class_name AssetBrowser
extends VBoxContainer

# ── Signals ──────────────────────────────────────────────────────
signal data_changed()  # Emitted when any in-memory data is modified

# ── Asset type enum ──────────────────────────────────────────────
enum AssetType { GRHS, BODIES, HEADS, HELMETS, WEAPONS, SHIELDS, FXS, RAW_FILE }

const TYPE_LABELS: Array[String] = [
	"GRHs (Graficos.ind)",
	"Cuerpos (Personajes.ind)",
	"Cabezas (Cabezas.ind)",
	"Cascos (Cascos.ind)",
	"Armas (Armas.dat)",
	"Escudos (Escudos.dat)",
	"FXs (Fxs.ind)",
	"Archivo INIT (texto)",
]

# ── External data (references to Main.gd arrays — shared memory) ──
var grh_data: Dictionary = {}           # {"entries": {grh_idx: {...}}, "max_index": N}
var graficos_folder: String = ""        # Path to Graficos/ for loading textures
var init_folder: String = ""            # Path to INIT/

var bodies_data: Array = []             # [{index, walk_n/e/s/w, head_x, head_y}]
var heads_data: Array = []              # [{index, head_n/e/s/w}]
var helmets_data: Array = []            # [{index, head_n/e/s/w}]
var weapons_data: Array = []            # [{index, dir_n/e/s/w}]
var shields_data: Array = []            # [{index, dir_n/e/s/w}]
var fxs_data: Array = []               # [{index, animacion, offset_x, offset_y}]

# ── UI elements ──────────────────────────────────────────────────
var _type_dropdown: OptionButton
var _search_box: LineEdit
var _content_split: HSplitContainer
var _item_list: ItemList
var _detail_panel: VBoxContainer
var _detail_scroll: ScrollContainer
var _preview_panel: FramePreviewPanel
var _fields_container: VBoxContainer
var _lbl_info: Label
var _btn_apply: Button
var _btn_delete: Button
var _btn_add: Button

# Raw file editor (for RAW_FILE mode)
var _raw_file_list: ItemList
var _raw_text_edit: TextEdit
var _raw_view: Control

var _current_type: int = AssetType.GRHS
var _selected_index: int = -1
var _filtered_indices: Array = []  # Maps list idx → data array idx (or grh_index for GRHs)
var _field_spins: Dictionary = {}  # field_name → SpinBox

# Animation preview state
var _anim_playing: bool = false
var _anim_time: float = 0.0
var _anim_fps: float = 6.0
var _anim_frame_idx: int = 0
var _anim_frames: Array = []
var _preview_textures: Dictionary = {}  # file_num → ImageTexture cache

# GRH list cache
var _grh_sorted_keys: Array = []

# Raw file state
var _raw_init_files: Array[String] = []
var _raw_current_path: String = ""


func _ready() -> void:
	add_theme_constant_override("separation", 4)

	# Type selector row
	var type_row := HBoxContainer.new()
	type_row.add_theme_constant_override("separation", 4)
	add_child(type_row)

	type_row.add_child(IndexerTheme.label("Ver:", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM))
	_type_dropdown = OptionButton.new()
	_type_dropdown.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	for lbl in TYPE_LABELS:
		_type_dropdown.add_item(lbl)
	_type_dropdown.selected = 0
	_type_dropdown.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_type_dropdown.item_selected.connect(_on_type_changed)
	type_row.add_child(_type_dropdown)

	# Search box
	_search_box = LineEdit.new()
	_search_box.placeholder_text = "Buscar por # o GRH..."
	_search_box.clear_button_enabled = true
	_search_box.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_search_box.text_changed.connect(func(_t): _rebuild_list())
	add_child(_search_box)

	# Info + action buttons row
	var info_row := HBoxContainer.new()
	info_row.add_theme_constant_override("separation", 4)
	add_child(info_row)

	_lbl_info = IndexerTheme.label("", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	_lbl_info.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_lbl_info.custom_minimum_size.y = 16
	info_row.add_child(_lbl_info)

	_btn_add = IndexerTheme.button("+ Nuevo", _on_add_entry)
	_btn_add.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM - 1)
	info_row.add_child(_btn_add)

	_btn_delete = IndexerTheme.button("Eliminar", _on_delete_entry, IndexerTheme.TEXT_DANGER)
	_btn_delete.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM - 1)
	_btn_delete.disabled = true
	info_row.add_child(_btn_delete)

	# Main content area
	_build_asset_view()
	_build_raw_view()
	_show_asset_mode(true)


func _build_asset_view() -> void:
	_content_split = HSplitContainer.new()
	_content_split.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_content_split.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_content_split.split_offset = 130
	add_child(_content_split)

	# Left: item list
	var left_vbox := VBoxContainer.new()
	left_vbox.add_theme_constant_override("separation", 0)
	left_vbox.custom_minimum_size.x = 110
	_content_split.add_child(left_vbox)

	_item_list = ItemList.new()
	_item_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_item_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_item_list.item_selected.connect(_on_item_selected)
	_item_list.allow_reselect = true
	left_vbox.add_child(_item_list)

	# Right: detail panel
	_detail_scroll = ScrollContainer.new()
	_detail_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_detail_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_content_split.add_child(_detail_scroll)

	_detail_panel = VBoxContainer.new()
	_detail_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail_panel.add_theme_constant_override("separation", 4)
	_detail_scroll.add_child(_detail_panel)

	# Preview
	_preview_panel = FramePreviewPanel.new()
	_preview_panel.custom_minimum_size = Vector2(120, 96)
	_preview_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail_panel.add_child(_preview_panel)

	# Fields
	_fields_container = VBoxContainer.new()
	_fields_container.add_theme_constant_override("separation", 3)
	_fields_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail_panel.add_child(_fields_container)

	# Apply button (writes to in-memory array only)
	_btn_apply = IndexerTheme.success_button("Aplicar", _on_apply_entry)
	_btn_apply.visible = false
	_detail_panel.add_child(_btn_apply)


func _build_raw_view() -> void:
	_raw_view = VBoxContainer.new()
	_raw_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_raw_view.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_raw_view.visible = false
	add_child(_raw_view)

	var raw_split := VSplitContainer.new()
	raw_split.size_flags_vertical = Control.SIZE_EXPAND_FILL
	raw_split.split_offset = 180
	_raw_view.add_child(raw_split)

	var raw_top := VBoxContainer.new()
	raw_top.add_theme_constant_override("separation", 3)
	raw_split.add_child(raw_top)

	_raw_file_list = ItemList.new()
	_raw_file_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_raw_file_list.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_raw_file_list.item_selected.connect(_on_raw_file_selected)
	raw_top.add_child(_raw_file_list)

	_raw_text_edit = TextEdit.new()
	_raw_text_edit.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_raw_text_edit.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
	_raw_text_edit.placeholder_text = "Selecciona un archivo INIT"
	_raw_text_edit.editable = false
	raw_split.add_child(_raw_text_edit)


func _show_asset_mode(asset: bool) -> void:
	_content_split.visible = asset
	_raw_view.visible = not asset
	_search_box.visible = asset
	_btn_add.visible = asset
	_btn_delete.visible = asset


# ── Public API ───────────────────────────────────────────────────

func refresh() -> void:
	if _current_type == AssetType.GRHS:
		_rebuild_grh_keys()
	_rebuild_list()


func load_init_files(folder: String) -> void:
	_raw_init_files.clear()
	_raw_file_list.clear()
	var dir := DirAccess.open(folder)
	if dir == null:
		return
	dir.list_dir_begin()
	var f := dir.get_next()
	while f != "":
		var ext := f.get_extension().to_lower()
		if ext in ["ind", "ini", "dat"]:
			_raw_init_files.append(folder.path_join(f))
		f = dir.get_next()
	dir.list_dir_end()
	_raw_init_files.sort()
	for path in _raw_init_files:
		_raw_file_list.add_item(path.get_file())


# ── Process (animation) ─────────────────────────────────────────

func _process(delta: float) -> void:
	if not _anim_playing or _anim_frames.is_empty():
		return
	_anim_time += delta
	var spf := 1.0 / _anim_fps if _anim_fps > 0 else 0.125
	if _anim_time >= spf:
		_anim_time -= spf
		_anim_frame_idx = (_anim_frame_idx + 1) % _anim_frames.size()
		_preview_panel.show_frame(_anim_frame_idx)


# ── Type change ──────────────────────────────────────────────────

func _on_type_changed(idx: int) -> void:
	_current_type = idx
	_selected_index = -1
	_clear_detail()
	_btn_delete.disabled = true

	if idx == AssetType.RAW_FILE:
		_show_asset_mode(false)
		_lbl_info.text = "%d archivos INIT" % _raw_init_files.size()
	else:
		_show_asset_mode(true)
		if idx == AssetType.GRHS:
			_rebuild_grh_keys()
		_rebuild_list()


# ── GRH key cache ───────────────────────────────────────────────

func _rebuild_grh_keys() -> void:
	_grh_sorted_keys.clear()
	var entries: Dictionary = grh_data.get("entries", {})
	for key in entries:
		_grh_sorted_keys.append(int(key))
	_grh_sorted_keys.sort()


# ── List building ────────────────────────────────────────────────

func _rebuild_list() -> void:
	_item_list.clear()
	_filtered_indices.clear()
	var search := _search_box.text.strip_edges().to_lower()

	if _current_type == AssetType.GRHS:
		_rebuild_grh_list(search)
	else:
		_rebuild_asset_list(search)


func _rebuild_grh_list(search: String) -> void:
	var entries: Dictionary = grh_data.get("entries", {})
	var count := 0
	var total := _grh_sorted_keys.size()
	const MAX_DISPLAY := 500

	for grh_idx in _grh_sorted_keys:
		if count >= MAX_DISPLAY:
			break
		var e: Dictionary = entries.get(grh_idx, {})
		var nf: int = e.get("num_frames", 1)

		if search.length() > 0:
			if not str(grh_idx).find(search) >= 0:
				continue

		var label: String
		if nf > 1:
			label = "G%d  Anim(%d frames)" % [grh_idx, nf]
		else:
			label = "G%d  F%d [%d,%d %dx%d]" % [
				grh_idx, e.get("file_num", 0),
				e.get("sx", 0), e.get("sy", 0),
				e.get("width", 0), e.get("height", 0)]

		_item_list.add_item(label)
		_filtered_indices.append(grh_idx)
		count += 1

	_lbl_info.text = "%d GRHs" % total
	if count < total:
		_lbl_info.text += " (mostrando %d, usa el buscador)" % count


func _rebuild_asset_list(search: String) -> void:
	var data := _get_current_data()

	for i in range(data.size()):
		var entry: Dictionary = data[i]
		var idx: int = entry.get("index", i + 1)
		var label := _make_entry_label(entry)

		if search.length() > 0:
			var search_str := str(idx) + " " + label.to_lower()
			if search_str.find(search) < 0:
				continue

		_item_list.add_item("#%d  %s" % [idx, label])
		_filtered_indices.append(i)

	_lbl_info.text = "%d entradas" % _filtered_indices.size()
	if _filtered_indices.size() < data.size():
		_lbl_info.text += " (de %d)" % data.size()


func _make_entry_label(entry: Dictionary) -> String:
	match _current_type:
		AssetType.BODIES:
			return "N:%d E:%d S:%d W:%d" % [
				entry.get("walk_n", 0), entry.get("walk_e", 0),
				entry.get("walk_s", 0), entry.get("walk_w", 0)]
		AssetType.HEADS, AssetType.HELMETS:
			return "N:%d E:%d S:%d W:%d" % [
				entry.get("head_n", 0), entry.get("head_e", 0),
				entry.get("head_s", 0), entry.get("head_w", 0)]
		AssetType.WEAPONS, AssetType.SHIELDS:
			return "N:%d E:%d S:%d W:%d" % [
				entry.get("dir_n", 0), entry.get("dir_e", 0),
				entry.get("dir_s", 0), entry.get("dir_w", 0)]
		AssetType.FXS:
			return "Anim:%d (%d,%d)" % [
				entry.get("animacion", 0),
				entry.get("offset_x", 0), entry.get("offset_y", 0)]
	return ""


func _get_current_data() -> Array:
	match _current_type:
		AssetType.BODIES: return bodies_data
		AssetType.HEADS: return heads_data
		AssetType.HELMETS: return helmets_data
		AssetType.WEAPONS: return weapons_data
		AssetType.SHIELDS: return shields_data
		AssetType.FXS: return fxs_data
	return []


# ── Item selection ───────────────────────────────────────────────

func _on_item_selected(list_idx: int) -> void:
	if list_idx < 0 or list_idx >= _filtered_indices.size():
		return

	_selected_index = list_idx
	_btn_delete.disabled = false

	if _current_type == AssetType.GRHS:
		var grh_idx: int = _filtered_indices[list_idx]
		var entry: Dictionary = grh_data.get("entries", {}).get(grh_idx, {})
		_build_grh_detail(grh_idx, entry)
	else:
		var data_idx: int = _filtered_indices[list_idx]
		var data := _get_current_data()
		if data_idx >= 0 and data_idx < data.size():
			_build_detail_for(data[data_idx])


func _clear_detail() -> void:
	_anim_playing = false
	_anim_frames.clear()
	_preview_panel.set_frames([])
	for c in _fields_container.get_children():
		c.queue_free()
	_field_spins.clear()
	_btn_apply.visible = false


# ── GRH detail ───────────────────────────────────────────────────

func _build_grh_detail(grh_idx: int, entry: Dictionary) -> void:
	_clear_detail()
	_btn_apply.visible = true

	var nf: int = entry.get("num_frames", 1)

	_add_field("grh_index", "GRH Index", grh_idx, 1, 99999)
	_field_spins["grh_index"].editable = false  # Read-only key

	if nf > 1:
		# Animated GRH
		_add_field("num_frames", "Num Frames", nf, 1, 999)
		_field_spins["num_frames"].editable = false
		_add_field("speed", "Velocidad", entry.get("speed", 0.0), 0, 9999)

		# Show frame indices as text
		var indices: Array = entry.get("frame_indices", [])
		var lbl := IndexerTheme.label("Frames: %s" % str(indices),
			IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM - 1)
		lbl.autowrap_mode = TextServer.AUTOWRAP_WORD
		_fields_container.add_child(lbl)

		_show_anim_preview(grh_idx)
	else:
		# Static GRH
		_add_field("file_num", "FileNum", entry.get("file_num", 0))
		_add_field("sx", "SX", entry.get("sx", 0), 0, 9999)
		_add_field("sy", "SY", entry.get("sy", 0), 0, 9999)
		_add_field("width", "Ancho", entry.get("width", 32), 1, 9999)
		_add_field("height", "Alto", entry.get("height", 32), 1, 9999)

		_show_static_preview(grh_idx)


# ── Asset detail ─────────────────────────────────────────────────

func _build_detail_for(entry: Dictionary) -> void:
	_clear_detail()
	_btn_apply.visible = true

	match _current_type:
		AssetType.BODIES:
			_add_field("walk_n", "Walk Norte (GRH)", entry.get("walk_n", 0))
			_add_field("walk_e", "Walk Este (GRH)", entry.get("walk_e", 0))
			_add_field("walk_s", "Walk Sur (GRH)", entry.get("walk_s", 0))
			_add_field("walk_w", "Walk Oeste (GRH)", entry.get("walk_w", 0))
			_add_field("head_x", "Head Offset X", entry.get("head_x", 0), -200, 200)
			_add_field("head_y", "Head Offset Y", entry.get("head_y", 0), -200, 200)
			_show_anim_preview(entry.get("walk_s", 0))

		AssetType.HEADS, AssetType.HELMETS:
			_add_field("head_n", "Norte (GRH)", entry.get("head_n", 0))
			_add_field("head_e", "Este (GRH)", entry.get("head_e", 0))
			_add_field("head_s", "Sur (GRH)", entry.get("head_s", 0))
			_add_field("head_w", "Oeste (GRH)", entry.get("head_w", 0))
			_show_static_preview(entry.get("head_s", 0))

		AssetType.WEAPONS, AssetType.SHIELDS:
			_add_field("dir_n", "Norte (GRH)", entry.get("dir_n", 0))
			_add_field("dir_e", "Este (GRH)", entry.get("dir_e", 0))
			_add_field("dir_s", "Sur (GRH)", entry.get("dir_s", 0))
			_add_field("dir_w", "Oeste (GRH)", entry.get("dir_w", 0))
			_show_anim_preview(entry.get("dir_s", 0))

		AssetType.FXS:
			_add_field("animacion", "Animación (GRH)", entry.get("animacion", 0))
			_add_field("offset_x", "Offset X", entry.get("offset_x", 0), -500, 500)
			_add_field("offset_y", "Offset Y", entry.get("offset_y", 0), -500, 500)
			_show_anim_preview(entry.get("animacion", 0))

	# Direction buttons for types with 4 directions
	if _current_type in [AssetType.BODIES, AssetType.HEADS, AssetType.HELMETS,
						  AssetType.WEAPONS, AssetType.SHIELDS]:
		_add_direction_buttons(entry)


func _add_field(key: String, label_text: String, value, min_val: int = 0, max_val: int = 99999) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	_fields_container.add_child(row)

	var lbl := IndexerTheme.label(label_text + ":", IndexerTheme.TEXT_SECONDARY, IndexerTheme.FONT_SIZE_SM)
	lbl.custom_minimum_size.x = 95
	row.add_child(lbl)

	var spin := IndexerTheme.spinbox(min_val, max_val, int(value))
	spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(spin)
	_field_spins[key] = spin


func _add_direction_buttons(entry: Dictionary) -> void:
	_fields_container.add_child(IndexerTheme.separator_h())

	var lbl := IndexerTheme.label("Preview dirección:", IndexerTheme.TEXT_MUTED, IndexerTheme.FONT_SIZE_SM)
	_fields_container.add_child(lbl)

	var dir_row := HBoxContainer.new()
	dir_row.add_theme_constant_override("separation", 4)
	dir_row.alignment = BoxContainer.ALIGNMENT_CENTER
	_fields_container.add_child(dir_row)

	var dirs: Array[String] = ["N", "E", "S", "W"]
	var grh_keys: Array[String]
	match _current_type:
		AssetType.BODIES:
			grh_keys = ["walk_n", "walk_e", "walk_s", "walk_w"]
		AssetType.HEADS, AssetType.HELMETS:
			grh_keys = ["head_n", "head_e", "head_s", "head_w"]
		_:
			grh_keys = ["dir_n", "dir_e", "dir_s", "dir_w"]

	for i in range(dirs.size()):
		var btn := Button.new()
		btn.text = dirs[i]
		btn.add_theme_font_size_override("font_size", IndexerTheme.FONT_SIZE_SM)
		btn.custom_minimum_size = Vector2(32, 24)
		var grh_key := grh_keys[i]
		var is_body := _current_type == AssetType.BODIES
		var is_head := _current_type in [AssetType.HEADS, AssetType.HELMETS]
		btn.pressed.connect(func():
			var grh_val: int
			if _field_spins.has(grh_key):
				grh_val = int(_field_spins[grh_key].value)
			else:
				grh_val = entry.get(grh_key, 0)
			if is_body:
				_show_anim_preview(grh_val)
			elif is_head:
				_show_static_preview(grh_val)
			else:
				_show_anim_preview(grh_val))
		dir_row.add_child(btn)


# ── Preview ──────────────────────────────────────────────────────

func _show_anim_preview(grh_index: int) -> void:
	_anim_playing = false
	_anim_frames.clear()
	_preview_panel.set_textures({})

	if grh_index <= 0 or grh_data.is_empty():
		_preview_panel.set_frames([])
		return

	var entry = grh_data.get("entries", {}).get(grh_index, null)
	if entry == null:
		_preview_panel.set_frames([])
		return

	var nf: int = entry.get("num_frames", 1)
	if nf <= 1:
		_show_static_preview(grh_index)
		return

	# Animated: resolve each sub-frame
	var frames: Array = []
	var needed_files: Dictionary = {}
	for fi in entry.get("frame_indices", []):
		var fe = grh_data.get("entries", {}).get(fi, null)
		if fe != null and fe.get("num_frames", 1) == 1:
			var fnum: int = fe.get("file_num", 0)
			frames.append({
				"sx": fe.get("sx", 0), "sy": fe.get("sy", 0),
				"w": fe.get("width", 32), "h": fe.get("height", 32),
				"file_num": fnum
			})
			needed_files[fnum] = true

	if frames.is_empty():
		_preview_panel.set_frames([])
		return

	var textures: Dictionary = {}
	for fnum in needed_files:
		textures[fnum] = _get_texture_for_file(fnum)

	_anim_frames = frames
	_preview_panel.set_textures(textures)
	_preview_panel.set_frames(frames)
	_preview_panel.show_frame(0)
	_anim_frame_idx = 0
	_anim_time = 0.0
	_anim_fps = entry.get("speed", 6.0)
	if _anim_fps <= 0:
		_anim_fps = 6.0
	_anim_playing = frames.size() > 1


func _show_static_preview(grh_index: int) -> void:
	_anim_playing = false
	_anim_frames.clear()

	if grh_index <= 0 or grh_data.is_empty():
		_preview_panel.set_frames([])
		return

	var entry = grh_data.get("entries", {}).get(grh_index, null)
	if entry == null:
		_preview_panel.set_frames([])
		return

	var fnum: int = entry.get("file_num", 0)
	var tex := _get_texture_for_file(fnum)
	_preview_panel.set_textures({fnum: tex})
	_preview_panel.set_frames([{
		"sx": entry.get("sx", 0), "sy": entry.get("sy", 0),
		"w": entry.get("width", 32), "h": entry.get("height", 32),
		"file_num": fnum
	}])
	_preview_panel.show_frame(0)


func _get_texture_for_file(file_num: int) -> ImageTexture:
	if _preview_textures.has(file_num):
		return _preview_textures[file_num]

	if graficos_folder.is_empty() or file_num <= 0:
		return null

	for ext in ["png", "bmp", "jpg", "jpeg", "tga"]:
		var path := graficos_folder.path_join("%d.%s" % [file_num, ext])
		if FileAccess.file_exists(path):
			var img := _load_image(path)
			if img != null:
				var tex := ImageTexture.create_from_image(img)
				_preview_textures[file_num] = tex
				return tex

	return null


func _load_image(path: String) -> Image:
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
	elif ext in ["jpg", "jpeg"]:
		err = img.load_jpg_from_buffer(bytes)
	elif ext == "bmp":
		err = img.load_bmp_from_buffer(bytes)
	elif ext == "tga":
		err = img.load_tga_from_buffer(bytes)
	else:
		return null

	return img if err == OK else null


# ── Apply entry (in-memory only) ─────────────────────────────────

func _on_apply_entry() -> void:
	if _selected_index < 0 or _selected_index >= _filtered_indices.size():
		return

	if _current_type == AssetType.GRHS:
		_apply_grh_entry()
	else:
		_apply_asset_entry()


func _apply_grh_entry() -> void:
	var grh_idx: int = _filtered_indices[_selected_index]
	var entries: Dictionary = grh_data.get("entries", {})
	if not entries.has(grh_idx):
		return

	var entry: Dictionary = entries[grh_idx]
	var nf: int = entry.get("num_frames", 1)

	if nf <= 1:
		for key in ["file_num", "sx", "sy", "width", "height"]:
			if _field_spins.has(key):
				entry[key] = int(_field_spins[key].value)
	else:
		if _field_spins.has("speed"):
			entry["speed"] = _field_spins["speed"].value

	data_changed.emit()

	# Refresh list label
	_rebuild_list()
	# Re-select
	for i in range(_filtered_indices.size()):
		if _filtered_indices[i] == grh_idx:
			_item_list.select(i)
			break


func _apply_asset_entry() -> void:
	var data_idx: int = _filtered_indices[_selected_index]
	var data := _get_current_data()
	if data_idx < 0 or data_idx >= data.size():
		return

	var entry: Dictionary = data[data_idx]
	for key in _field_spins:
		entry[key] = int(_field_spins[key].value)

	data_changed.emit()

	# Refresh list label
	var list_idx := _selected_index
	if list_idx >= 0 and list_idx < _item_list.item_count:
		_item_list.set_item_text(list_idx, "#%d  %s" % [entry.get("index", data_idx + 1), _make_entry_label(entry)])


# ── Delete entry ─────────────────────────────────────────────────

func _on_delete_entry() -> void:
	if _selected_index < 0 or _selected_index >= _filtered_indices.size():
		return

	if _current_type == AssetType.GRHS:
		var grh_idx: int = _filtered_indices[_selected_index]
		var entries: Dictionary = grh_data.get("entries", {})
		if entries.has(grh_idx):
			entries.erase(grh_idx)
			_grh_sorted_keys.erase(grh_idx)
	else:
		var data_idx: int = _filtered_indices[_selected_index]
		var data := _get_current_data()
		if data_idx >= 0 and data_idx < data.size():
			data.remove_at(data_idx)
			# Re-index remaining entries
			for i in range(data.size()):
				data[i]["index"] = i + 1

	_clear_detail()
	_btn_delete.disabled = true
	_selected_index = -1
	data_changed.emit()
	_rebuild_list()


# ── Add new entry ────────────────────────────────────────────────

func _on_add_entry() -> void:
	if _current_type == AssetType.GRHS:
		_add_grh_entry()
	elif _current_type == AssetType.RAW_FILE:
		return
	else:
		_add_asset_entry()

	data_changed.emit()
	_rebuild_list()

	# Select the last entry
	if _item_list.item_count > 0:
		_item_list.select(_item_list.item_count - 1)
		_on_item_selected(_item_list.item_count - 1)


func _add_grh_entry() -> void:
	var max_idx: int = grh_data.get("max_index", 0)
	var new_idx := max_idx + 1
	grh_data["entries"][new_idx] = {
		"grh_index": new_idx, "num_frames": 1,
		"file_num": 1, "sx": 0, "sy": 0, "width": 32, "height": 32
	}
	grh_data["max_index"] = new_idx
	_grh_sorted_keys.append(new_idx)


func _add_asset_entry() -> void:
	var data := _get_current_data()
	var new_idx: int = data.size() + 1
	var entry: Dictionary = {"index": new_idx}

	match _current_type:
		AssetType.BODIES:
			entry.merge({"walk_n": 0, "walk_e": 0, "walk_s": 0, "walk_w": 0, "head_x": 0, "head_y": 0})
		AssetType.HEADS, AssetType.HELMETS:
			entry.merge({"head_n": 0, "head_e": 0, "head_s": 0, "head_w": 0})
		AssetType.WEAPONS, AssetType.SHIELDS:
			entry.merge({"dir_n": 0, "dir_e": 0, "dir_s": 0, "dir_w": 0})
		AssetType.FXS:
			entry.merge({"animacion": 0, "offset_x": 0, "offset_y": 0})

	data.append(entry)


# ── Raw file mode ────────────────────────────────────────────────

func _on_raw_file_selected(idx: int) -> void:
	if idx < 0 or idx >= _raw_init_files.size():
		return
	_load_raw_file(_raw_init_files[idx])


func _load_raw_file(path: String) -> void:
	_raw_current_path = path
	var ext := path.get_extension().to_lower()

	if ext == "ind":
		_raw_text_edit.text = _parse_ind_to_text(path)
		_raw_text_edit.editable = false
		return

	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		_raw_text_edit.text = "(no se pudo abrir)"
		return
	_raw_text_edit.text = f.get_as_text()
	_raw_text_edit.editable = false  # Read-only — edits go through the structured views
	f.close()


func _parse_ind_to_text(path: String) -> String:
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return "(no se pudo abrir)"
	if f.get_length() < 265:
		f.close()
		return "(archivo muy pequeño)"

	var filename := path.get_file().to_lower()
	f.seek(263)
	var count: int = f.get_16()
	var lines: PackedStringArray = ["# %s — %d entradas" % [path.get_file(), count], ""]

	if "personajes" in filename:
		for i in range(1, count + 1):
			lines.append("#%d: walk_n=%d walk_e=%d walk_s=%d walk_w=%d head_x=%d head_y=%d" % [
				i, _ri16(f), _ri16(f), _ri16(f), _ri16(f), _ri16(f), _ri16(f)])
	elif "cabezas" in filename or "cascos" in filename:
		for i in range(1, count + 1):
			lines.append("#%d: n=%d e=%d s=%d w=%d" % [i, _ri16(f), _ri16(f), _ri16(f), _ri16(f)])
	elif "fxs" in filename:
		for i in range(1, count + 1):
			lines.append("#%d: anim=%d off_x=%d off_y=%d" % [i, _ri16(f), _ri16(f), _ri16(f)])
	else:
		lines.append("(formato binario desconocido)")

	f.close()
	return "\n".join(lines)


static func _ri16(f: FileAccess) -> int:
	var val := f.get_16()
	if val >= 0x8000:
		return val - 0x10000
	return val
