#!/usr/bin/env python3
"""
Copy border tiles between adjacent maps for seamless transitions.
Only copies graphics visible in the viewport (±8 X, ±6 Y).
Preserves destination's blocked flags and tile exits.

Map28 adjacency:
  North: Map90 (exit Map28 Y=7 -> Map90 Y=90)
  South: Map18 (exit Map28 Y=94 -> Map18 Y=8) — already done
  West:  Map14 (exit Map28 X=9 -> Map14 X=89)
  East:  Map54 (exit Map28 X=92 -> Map54 X=12)
"""
import struct
import sys
import os
import shutil

SERVER_MAP_DIR = os.path.join(os.path.dirname(__file__), '..', 'server', 'maps')
CLIENT_MAP_DIR = os.path.join(os.path.dirname(__file__), '..', 'client', 'Data', 'Maps')
MAP_DIR = SERVER_MAP_DIR  # default, overridden by --client flag
MAP_SIZE = 100
HEADER_SIZE_MAP = 273
HEADER_SIZE_INF = 10


# ── .map parsing ──────────────────────────────────────────────

def parse_map_tile(data, pos):
    """Parse one tile from .map, return (tile_bytes, new_pos, byflags).
    All values are i16 (2 bytes LE). Light = range(2) + R(2) + G(2) + B(2) = 8 bytes.
    """
    start = pos
    flags = data[pos]; pos += 1
    pos += 2  # graphic1 (always present, i16)
    if flags & 2: pos += 2   # graphic2 (i16)
    if flags & 4: pos += 2   # graphic3 (i16)
    if flags & 8: pos += 2   # graphic4 (i16)
    if flags & 16: pos += 2  # trigger (i16)
    if flags & 32: pos += 2  # particle group index (i16)
    if flags & 64: pos += 8  # light: range(i16) + R(i16) + G(i16) + B(i16)
    return data[start:pos], pos, flags


def strip_blocked_flag(tile_bytes):
    """Return tile bytes with blocked flag (bit 0) cleared."""
    if not tile_bytes:
        return tile_bytes
    new_flags = tile_bytes[0] & ~1
    return bytes([new_flags]) + tile_bytes[1:]


def set_blocked_flag(tile_bytes, blocked):
    """Set or clear the blocked flag on tile bytes."""
    if not tile_bytes:
        return tile_bytes
    if blocked:
        new_flags = tile_bytes[0] | 1
    else:
        new_flags = tile_bytes[0] & ~1
    return bytes([new_flags]) + tile_bytes[1:]


def parse_all_map_tiles(path):
    """Parse .map file into header + 100x100 grid of (tile_bytes, byflags)."""
    with open(path, 'rb') as f:
        data = f.read()
    header = data[:HEADER_SIZE_MAP]
    pos = HEADER_SIZE_MAP
    tiles = {}  # (x, y) -> (tile_bytes, byflags)
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            tile_bytes, pos, flags = parse_map_tile(data, pos)
            tiles[(x, y)] = (tile_bytes, flags)
    return header, tiles


def rebuild_map(header, tiles):
    """Rebuild .map file from header + tile dict."""
    parts = [header]
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            parts.append(tiles[(x, y)][0])
    return b''.join(parts)


# ── .inf parsing ──────────────────────────────────────────────

def parse_inf_tile(data, pos):
    """Parse one .inf tile, return (raw_bytes, new_pos, flags, exit_data, npc_data, obj_data)."""
    start = pos
    flags = data[pos]; pos += 1
    exit_data = None
    npc_data = None
    obj_data = None
    if flags & 1:
        exit_data = data[pos:pos+6]; pos += 6
    if flags & 2:
        npc_data = data[pos:pos+2]; pos += 2
    if flags & 4:
        obj_data = data[pos:pos+4]; pos += 4
    return data[start:pos], pos, flags, exit_data, npc_data, obj_data


def build_inf_tile(flags, exit_data, npc_data, obj_data):
    """Build .inf tile bytes from components."""
    parts = [bytes([flags])]
    if flags & 1 and exit_data:
        parts.append(exit_data)
    if flags & 2 and npc_data:
        parts.append(npc_data)
    if flags & 4 and obj_data:
        parts.append(obj_data)
    return b''.join(parts)


def parse_all_inf_tiles(path):
    """Parse .inf file into header + 100x100 grid of tile components."""
    with open(path, 'rb') as f:
        data = f.read()
    header = data[:HEADER_SIZE_INF]
    pos = HEADER_SIZE_INF
    tiles = {}  # (x, y) -> (flags, exit_data, npc_data, obj_data)
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            _, pos, flags, exit_data, npc_data, obj_data = parse_inf_tile(data, pos)
            tiles[(x, y)] = (flags, exit_data, npc_data, obj_data)
    return header, tiles


def rebuild_inf(header, tiles):
    """Rebuild .inf file from header + tile dict."""
    parts = [header]
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            flags, exit_data, npc_data, obj_data = tiles[(x, y)]
            parts.append(build_inf_tile(flags, exit_data, npc_data, obj_data))
    return b''.join(parts)


# ── Border copy logic ────────────────────────────────────────

def copy_border(src_map_num, dst_map_num, tile_pairs, dry_run=False):
    """
    Copy tiles from source map to destination map.
    tile_pairs: list of ((src_x, src_y), (dst_x, dst_y))
    Preserves destination's blocked flags and tile exits.
    """
    src_map_path = os.path.join(MAP_DIR, f'Mapa{src_map_num}.map')
    src_inf_path = os.path.join(MAP_DIR, f'Mapa{src_map_num}.inf')
    dst_map_path = os.path.join(MAP_DIR, f'Mapa{dst_map_num}.map')
    dst_inf_path = os.path.join(MAP_DIR, f'Mapa{dst_map_num}.inf')

    print(f"\n{'='*60}")
    print(f"Copying {len(tile_pairs)} tiles: Map{src_map_num} -> Map{dst_map_num}")
    print(f"{'='*60}")

    # Parse source and destination .map files
    src_hdr, src_tiles = parse_all_map_tiles(src_map_path)
    dst_hdr, dst_tiles = parse_all_map_tiles(dst_map_path)

    # Parse source and destination .inf files (skip if empty/missing — client maps have no .inf)
    has_inf = (os.path.exists(src_inf_path) and os.path.getsize(src_inf_path) > HEADER_SIZE_INF
               and os.path.exists(dst_inf_path) and os.path.getsize(dst_inf_path) > HEADER_SIZE_INF)
    src_inf_hdr = dst_inf_hdr = None
    src_inf_tiles = dst_inf_tiles = None
    if has_inf:
        src_inf_hdr, src_inf_tiles = parse_all_inf_tiles(src_inf_path)
        dst_inf_hdr, dst_inf_tiles = parse_all_inf_tiles(dst_inf_path)

    tiles_copied = 0
    npcs_copied = 0
    objs_copied = 0

    for (sx, sy), (dx, dy) in tile_pairs:
        # ── .map: copy graphics, preserve blocked flag ──
        src_tile_bytes, src_flags = src_tiles[(sx, sy)]
        dst_tile_bytes, dst_flags = dst_tiles[(dx, dy)]

        # Preserve destination's blocked state
        dst_blocked = bool(dst_flags & 1)
        new_tile = strip_blocked_flag(src_tile_bytes)
        new_tile = set_blocked_flag(new_tile, dst_blocked)
        new_flags = (src_flags & ~1) | (1 if dst_blocked else 0)
        dst_tiles[(dx, dy)] = (new_tile, new_flags)

        # ── .inf: copy NPCs/objects, preserve exits (skip if no .inf) ──
        if has_inf:
            src_inf_flags, src_exit, src_npc, src_obj = src_inf_tiles[(sx, sy)]
            dst_inf_flags, dst_exit, dst_npc, dst_obj = dst_inf_tiles[(dx, dy)]

            new_inf_flags = 0
            new_exit = None
            new_npc = None
            new_obj = None

            if dst_inf_flags & 1 and dst_exit:
                new_inf_flags |= 1
                new_exit = dst_exit

            if src_inf_flags & 2 and src_npc:
                new_inf_flags |= 2
                new_npc = src_npc
                npcs_copied += 1
            elif dst_inf_flags & 2 and dst_npc:
                new_inf_flags |= 2
                new_npc = dst_npc

            if src_inf_flags & 4 and src_obj:
                new_inf_flags |= 4
                new_obj = src_obj
                objs_copied += 1
            elif dst_inf_flags & 4 and dst_obj:
                new_inf_flags |= 4
                new_obj = dst_obj

            dst_inf_tiles[(dx, dy)] = (new_inf_flags, new_exit, new_npc, new_obj)
        tiles_copied += 1

    print(f"  Tiles: {tiles_copied}, NPCs: {npcs_copied}, Objects: {objs_copied}")

    if dry_run:
        print("  [DRY RUN] Not writing files.")
        return

    # Backup and write .map
    shutil.copy2(dst_map_path, dst_map_path + '.bak')
    new_map = rebuild_map(dst_hdr, dst_tiles)
    with open(dst_map_path, 'wb') as f:
        f.write(new_map)
    print(f"  Wrote {dst_map_path} ({len(new_map)} bytes)")

    # Backup and write .inf (only if we have valid inf data)
    if has_inf:
        shutil.copy2(dst_inf_path, dst_inf_path + '.bak')
        new_inf = rebuild_inf(dst_inf_hdr, dst_inf_tiles)
        with open(dst_inf_path, 'wb') as f:
            f.write(new_inf)
        print(f"  Wrote {dst_inf_path} ({len(new_inf)} bytes)")
    else:
        print(f"  Skipped .inf (empty or missing)")


def make_row_pairs(src_rows, dst_rows, x_range=(1, 100)):
    """Generate tile pairs for horizontal row copy (full width or partial)."""
    pairs = []
    for sr, dr in zip(src_rows, dst_rows):
        for x in range(x_range[0], x_range[1] + 1):
            pairs.append(((x, sr), (x, dr)))
    return pairs


def make_col_pairs(src_cols, dst_cols, y_range=(1, 100)):
    """Generate tile pairs for vertical column copy (full height or partial)."""
    pairs = []
    for sc, dc in zip(src_cols, dst_cols):
        for y in range(y_range[0], y_range[1] + 1):
            pairs.append(((sc, y), (dc, y)))
    return pairs


def main():
    global MAP_DIR
    dry_run = '--dry-run' in sys.argv
    if '--client' in sys.argv:
        MAP_DIR = CLIENT_MAP_DIR
        print(f"Using CLIENT maps: {MAP_DIR}")
    elif '--server' in sys.argv:
        MAP_DIR = SERVER_MAP_DIR
        print(f"Using SERVER maps: {MAP_DIR}")
    else:
        # Do both
        for target in ['--server', '--client']:
            print(f"\n{'#'*60}")
            print(f"# Running with {target}")
            print(f"{'#'*60}")
            MAP_DIR = SERVER_MAP_DIR if target == '--server' else CLIENT_MAP_DIR
            run_copies(dry_run)
        return

    run_copies(dry_run)


def run_copies(dry_run=False):

    # NORTH: Map28 -> Map90
    # Exit: Map28 Y=7 -> Map90 Y=90
    # At Map90 Y=90, viewport south: Y=91..96 (6 tiles behind player)
    # These should look like Map28 Y=8..13 (6 tiles south of exit)
    north_pairs = make_row_pairs(
        src_rows=range(8, 14),    # Map28 Y=8..13
        dst_rows=range(91, 97),   # Map90 Y=91..96
    )

    # WEST: Map28 -> Map14
    # Exit: Map28 X=9 -> Map14 X=89
    # At Map14 X=89, viewport east: X=90..97 (8 tiles behind player)
    # These should look like Map28 X=10..17 (8 tiles east of exit)
    west_pairs = make_col_pairs(
        src_cols=range(10, 18),   # Map28 X=10..17
        dst_cols=range(90, 98),   # Map14 X=90..97
    )

    # EAST: Map28 -> Map54
    # Exit: Map28 X=92 -> Map54 X=12
    # At Map54 X=12, viewport west: X=4..11 (8 tiles behind player)
    # These should look like Map28 X=84..91 (8 tiles west of exit)
    east_pairs = make_col_pairs(
        src_cols=range(84, 92),   # Map28 X=84..91
        dst_cols=range(4, 12),    # Map54 X=4..11
    )

    print("Map28 Border Copy — Viewport Only")
    print(f"North: 6 rows × 100 cols = {len(north_pairs)} tiles (Map28 Y=8..13 -> Map90 Y=91..96)")
    print(f"West:  8 cols × 100 rows = {len(west_pairs)} tiles (Map28 X=10..17 -> Map14 X=90..97)")
    print(f"East:  8 cols × 100 rows = {len(east_pairs)} tiles (Map28 X=84..91 -> Map54 X=4..11)")

    copy_border(28, 90, north_pairs, dry_run)
    copy_border(28, 14, west_pairs, dry_run)
    copy_border(28, 54, east_pairs, dry_run)

    print("\nDone with this target!")


if __name__ == '__main__':
    main()
