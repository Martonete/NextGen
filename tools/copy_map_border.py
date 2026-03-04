#!/usr/bin/env python3
"""
Copy the south border of Mapa28 (rows 86-100) to the north border of Mapa18 (rows 1-15).

.map format:
  - 273-byte header
  - 100x100 tiles, Y-major order (Y=1..100, X=1..100)
  - Each tile: 1 byte ByFlags + variable data
    - Always: Graphic[1] (i16 LE)
    - bit 0x01: blocked (no extra data)
    - bit 0x02: Graphic[2] (i16 LE)
    - bit 0x04: Graphic[3] (i16 LE)
    - bit 0x08: Graphic[4] (i16 LE)
    - bit 0x10: Trigger (i16 LE)
    - bit 0x20: ParticleGroupIndex (i16 LE)
    - bit 0x40: RangeLight(i16) + R(i16) + G(i16) + B(i16)

.inf format:
  - 10-byte header
  - 100x100 tiles, Y-major order
  - Each tile: 1 byte ByFlags + variable data
    - bit 0x01: TileExit — Map(i16) + X(i16) + Y(i16)
    - bit 0x02: NpcIndex (i16 LE)
    - bit 0x04: ObjIndex(i16) + Amount(i16)
"""

import struct
import sys
import os

MAP_HEADER_SIZE = 273
INF_HEADER_SIZE = 10
MAP_WIDTH = 100
MAP_HEIGHT = 100

# Rows to copy: Mapa28 Y=86..100 (0-indexed 85..99) -> Mapa18 Y=1..15 (0-indexed 0..14)
SRC_ROW_START = 85  # 0-indexed
SRC_ROW_END = 100   # exclusive
DST_ROW_START = 0
COPY_COUNT = 15


def parse_map_tile(data, offset):
    """Parse one .map tile starting at offset. Returns (tile_bytes, new_offset)."""
    start = offset
    byflags = data[offset]
    offset += 1
    # Graphic[1] always present
    offset += 2
    if byflags & 0x02:
        offset += 2  # Graphic[2]
    if byflags & 0x04:
        offset += 2  # Graphic[3]
    if byflags & 0x08:
        offset += 2  # Graphic[4]
    if byflags & 0x10:
        offset += 2  # Trigger
    if byflags & 0x20:
        offset += 2  # ParticleGroupIndex
    if byflags & 0x40:
        offset += 8  # RangeLight + RGB (4 x i16)
    return data[start:offset], offset


def parse_map_rows(data):
    """Parse all 100 rows of .map tile data. Returns list of 100 rows, each row = list of 100 tile byte strings."""
    offset = MAP_HEADER_SIZE
    rows = []
    for y in range(MAP_HEIGHT):
        row = []
        for x in range(MAP_WIDTH):
            tile_bytes, offset = parse_map_tile(data, offset)
            row.append(tile_bytes)
        rows.append(row)
    return rows


def parse_inf_tile(data, offset):
    """Parse one .inf tile. Returns (byflags, tile_exit, npc, obj, tile_bytes, new_offset)."""
    start = offset
    byflags = data[offset]
    offset += 1

    tile_exit = None
    npc = None
    obj = None

    if byflags & 0x01:
        tile_exit = data[offset:offset + 6]
        offset += 6
    if byflags & 0x02:
        npc = data[offset:offset + 2]
        offset += 2
    if byflags & 0x04:
        obj = data[offset:offset + 4]
        offset += 4

    return byflags, tile_exit, npc, obj, data[start:offset], offset


def parse_inf_rows(data):
    """Parse all 100 rows of .inf tile data. Returns list of 100 rows, each row = list of (byflags, exit, npc, obj) tuples."""
    offset = INF_HEADER_SIZE
    rows = []
    for y in range(MAP_HEIGHT):
        row = []
        for x in range(MAP_WIDTH):
            byflags, tile_exit, npc, obj, raw, offset = parse_inf_tile(data, offset)
            row.append((byflags, tile_exit, npc, obj))
        rows.append(row)
    return rows


def build_inf_tile(byflags, tile_exit, npc, obj):
    """Rebuild a single .inf tile from components."""
    result = bytes([byflags])
    if byflags & 0x01:
        result += tile_exit
    if byflags & 0x02:
        result += npc
    if byflags & 0x04:
        result += obj
    return result


def main():
    root = os.path.join(os.path.dirname(__file__), "..")

    # Primary: server-rust maps (what the Rust server loads)
    server_maps = os.path.join(root, "server-rust", "server", "maps")
    # Also update: VB6 server, client copies
    vb6_maps = os.path.join(root, "Servidor", "Maps")
    client_maps_rust = os.path.join(root, "server-rust", "client", "Data", "Maps")
    client_maps_vb6 = os.path.join(root, "Cliente", "Data", "MAPAS")

    src_map_path = os.path.join(server_maps, "Mapa28.map")
    src_inf_path = os.path.join(server_maps, "Mapa28.inf")
    dst_map_path = os.path.join(server_maps, "Mapa18.map")
    dst_inf_path = os.path.join(server_maps, "Mapa18.inf")

    # Read files
    with open(src_map_path, "rb") as f:
        src_map_data = f.read()
    with open(dst_map_path, "rb") as f:
        dst_map_data = f.read()
    with open(src_inf_path, "rb") as f:
        src_inf_data = f.read()
    with open(dst_inf_path, "rb") as f:
        dst_inf_data = f.read()

    print(f"Source: Mapa28.map ({len(src_map_data)} bytes), Mapa28.inf ({len(src_inf_data)} bytes)")
    print(f"Dest:   Mapa18.map ({len(dst_map_data)} bytes), Mapa18.inf ({len(dst_inf_data)} bytes)")

    # Parse .map files
    src_map_rows = parse_map_rows(src_map_data)
    dst_map_rows = parse_map_rows(dst_map_data)

    # Copy rows in .map: src rows 85..99 -> dst rows 0..14
    for i in range(COPY_COUNT):
        dst_map_rows[DST_ROW_START + i] = src_map_rows[SRC_ROW_START + i]

    # Rebuild Mapa18.map: original header + modified tiles
    new_map = bytearray(dst_map_data[:MAP_HEADER_SIZE])
    for row in dst_map_rows:
        for tile in row:
            new_map.extend(tile)

    # Parse .inf files
    src_inf_rows = parse_inf_rows(src_inf_data)
    dst_inf_rows = parse_inf_rows(dst_inf_data)

    # Merge .inf: copy NPCs and objects from src, but PRESERVE dst tile exits
    for i in range(COPY_COUNT):
        src_row = src_inf_rows[SRC_ROW_START + i]
        dst_row = dst_inf_rows[DST_ROW_START + i]
        merged_row = []
        for x in range(MAP_WIDTH):
            src_flags, src_exit, src_npc, src_obj = src_row[x]
            dst_flags, dst_exit, dst_npc, dst_obj = dst_row[x]

            # Use src's NPC and object data, but dst's tile exit
            new_exit = dst_exit  # preserve Mapa18's exits
            new_npc = src_npc   # copy Mapa28's NPCs
            new_obj = src_obj   # copy Mapa28's objects

            # Rebuild byflags
            new_flags = 0
            if new_exit is not None:
                new_flags |= 0x01
            if new_npc is not None:
                new_flags |= 0x02
            if new_obj is not None:
                new_flags |= 0x04

            merged_row.append((new_flags, new_exit, new_npc, new_obj))
        dst_inf_rows[DST_ROW_START + i] = merged_row

    # Rebuild Mapa18.inf: original header + modified tiles
    new_inf = bytearray(dst_inf_data[:INF_HEADER_SIZE])
    for row in dst_inf_rows:
        for tile in row:
            byflags, tile_exit, npc, obj = tile
            new_inf.extend(build_inf_tile(byflags, tile_exit, npc, obj))

    # Write output
    with open(dst_map_path, "wb") as f:
        f.write(new_map)
    with open(dst_inf_path, "wb") as f:
        f.write(new_inf)

    print(f"\nOutput: Mapa18.map ({len(new_map)} bytes), Mapa18.inf ({len(new_inf)} bytes)")
    print(f"Delta:  .map {len(new_map) - len(dst_map_data):+d} bytes, .inf {len(new_inf) - len(dst_inf_data):+d} bytes")

    # Sync to all other map locations
    for extra_dir in [vb6_maps, client_maps_rust, client_maps_vb6]:
        map_copy = os.path.join(extra_dir, "Mapa18.map")
        if os.path.exists(map_copy):
            import shutil
            shutil.copy2(dst_map_path, map_copy)
            print(f"Synced .map → {map_copy}")

    # .inf only exists in server dirs, not client
    for extra_dir in [vb6_maps]:
        inf_copy = os.path.join(extra_dir, "Mapa18.inf")
        if os.path.exists(inf_copy):
            import shutil
            shutil.copy2(dst_inf_path, inf_copy)
            print(f"Synced .inf → {inf_copy}")

    # Verify: re-parse output to check integrity
    try:
        verify_map_rows = parse_map_rows(bytes(new_map))
        verify_inf_rows = parse_inf_rows(bytes(new_inf))
        total_map_tiles = sum(len(r) for r in verify_map_rows)
        total_inf_tiles = sum(len(r) for r in verify_inf_rows)
        print(f"Verify: {total_map_tiles} map tiles, {total_inf_tiles} inf tiles — OK")
    except Exception as e:
        print(f"VERIFY FAILED: {e}", file=sys.stderr)
        sys.exit(1)

    # Summary of what changed in .inf
    exits_preserved = 0
    npcs_copied = 0
    objs_copied = 0
    for i in range(COPY_COUNT):
        for x in range(MAP_WIDTH):
            flags, exit_data, npc, obj = dst_inf_rows[DST_ROW_START + i][x]
            if exit_data:
                exits_preserved += 1
            if npc:
                npcs_copied += 1
            if obj:
                objs_copied += 1
    print(f"INF merge: {exits_preserved} exits preserved, {npcs_copied} NPCs copied, {objs_copied} objects copied")
    print("\nDone. Backups: Mapa18.map.bak, Mapa18.inf.bak")


if __name__ == "__main__":
    main()
