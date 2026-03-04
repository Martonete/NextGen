#!/usr/bin/env python3
"""
Fix border tile exits for Map28 adjacency (north/west/east) to match
the Map28<->Map18 pattern: 1-tile separation between exit and return.
Also re-copies border tiles with +2 extra tiles for better viewport coverage.

Skips exits on blocked tiles.
Applies to both server and client maps.

Map28 adjacency reference (Map28<->Map18 = working model):
  Map28 Y=94 -> Map18 Y=8   (exit row in source)
  Map18 Y=7  -> Map28 Y=93  (return: 1 tile before arrival, dest = exit-1)
"""
import struct
import sys
import os
import shutil

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SERVER_MAP_DIR = os.path.join(SCRIPT_DIR, '..', 'server', 'maps')
CLIENT_MAP_DIR = os.path.join(SCRIPT_DIR, '..', 'client', 'Data', 'Maps')
MAP_SIZE = 100
HEADER_SIZE_MAP = 273
HEADER_SIZE_INF = 10


# ── .map parsing ──────────────────────────────────────────────

def parse_map_tile(data, pos):
    start = pos
    flags = data[pos]; pos += 1
    pos += 2  # graphic1 (i16, always present)
    if flags & 2: pos += 2
    if flags & 4: pos += 2
    if flags & 8: pos += 2
    if flags & 16: pos += 2
    if flags & 32: pos += 2
    if flags & 64: pos += 8
    return data[start:pos], pos, flags


def parse_all_map_tiles(path):
    with open(path, 'rb') as f:
        data = f.read()
    header = data[:HEADER_SIZE_MAP]
    pos = HEADER_SIZE_MAP
    tiles = {}
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            tile_bytes, pos, flags = parse_map_tile(data, pos)
            tiles[(x, y)] = (tile_bytes, flags)
    return header, tiles


def rebuild_map(header, tiles):
    parts = [header]
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            parts.append(tiles[(x, y)][0])
    return b''.join(parts)


def is_blocked(tiles, x, y):
    """Check if a tile is blocked in .map data."""
    if (x, y) not in tiles:
        return True
    return bool(tiles[(x, y)][1] & 1)


# ── .inf parsing ──────────────────────────────────────────────

def parse_all_inf_tiles(path):
    with open(path, 'rb') as f:
        data = f.read()
    if len(data) <= HEADER_SIZE_INF:
        return None, None
    header = data[:HEADER_SIZE_INF]
    pos = HEADER_SIZE_INF
    tiles = {}
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            flags = data[pos]; pos += 1
            exit_data = npc_data = obj_data = None
            if flags & 1:
                exit_data = data[pos:pos+6]; pos += 6
            if flags & 2:
                npc_data = data[pos:pos+2]; pos += 2
            if flags & 4:
                obj_data = data[pos:pos+4]; pos += 4
            tiles[(x, y)] = (flags, exit_data, npc_data, obj_data)
    return header, tiles


def rebuild_inf(header, tiles):
    parts = [header]
    for y in range(1, MAP_SIZE + 1):
        for x in range(1, MAP_SIZE + 1):
            flags, exit_data, npc_data, obj_data = tiles[(x, y)]
            parts.append(bytes([flags]))
            if flags & 1 and exit_data:
                parts.append(exit_data)
            if flags & 2 and npc_data:
                parts.append(npc_data)
            if flags & 4 and obj_data:
                parts.append(obj_data)
    return b''.join(parts)


def make_exit_data(dest_map, dest_x, dest_y):
    """Create 6-byte tile exit data: map(i16) + x(i16) + y(i16)."""
    return struct.pack('<hhh', dest_map, dest_x, dest_y)


def set_exit(inf_tiles, x, y, dest_map, dest_x, dest_y):
    """Set a tile exit, preserving NPC/object data."""
    flags, _, npc_data, obj_data = inf_tiles[(x, y)]
    new_flags = flags | 1  # set exit bit
    exit_data = make_exit_data(dest_map, dest_x, dest_y)
    inf_tiles[(x, y)] = (new_flags, exit_data, npc_data, obj_data)


def clear_exit(inf_tiles, x, y):
    """Remove a tile exit, preserving NPC/object data."""
    flags, _, npc_data, obj_data = inf_tiles[(x, y)]
    new_flags = flags & ~1  # clear exit bit
    inf_tiles[(x, y)] = (new_flags, None, npc_data, obj_data)


def get_exit(inf_tiles, x, y):
    """Get exit data for a tile, returns (dest_map, dest_x, dest_y) or None."""
    flags, exit_data, _, _ = inf_tiles[(x, y)]
    if flags & 1 and exit_data:
        return struct.unpack('<hhh', exit_data)
    return None


# ── Border tile copy ─────────────────────────────────────────

def strip_blocked_flag(tile_bytes):
    if not tile_bytes:
        return tile_bytes
    return bytes([tile_bytes[0] & ~1]) + tile_bytes[1:]


def set_blocked_flag(tile_bytes, blocked):
    if not tile_bytes:
        return tile_bytes
    if blocked:
        return bytes([tile_bytes[0] | 1]) + tile_bytes[1:]
    return bytes([tile_bytes[0] & ~1]) + tile_bytes[1:]


def copy_tiles(src_tiles, dst_tiles, tile_pairs):
    """Copy tile graphics from src to dst, preserving dst blocked flags."""
    copied = 0
    for (sx, sy), (dx, dy) in tile_pairs:
        src_bytes, src_flags = src_tiles[(sx, sy)]
        _, dst_flags = dst_tiles[(dx, dy)]
        dst_blocked = bool(dst_flags & 1)
        new_bytes = set_blocked_flag(strip_blocked_flag(src_bytes), dst_blocked)
        new_flags = (src_flags & ~1) | (1 if dst_blocked else 0)
        dst_tiles[(dx, dy)] = (new_bytes, new_flags)
        copied += 1
    return copied


def copy_inf_tiles(src_inf, dst_inf, tile_pairs):
    """Copy NPCs/objects from src to dst inf, preserving dst exits."""
    if src_inf is None or dst_inf is None:
        return 0, 0
    npcs = objs = 0
    for (sx, sy), (dx, dy) in tile_pairs:
        sf, se, sn, so = src_inf[(sx, sy)]
        df, de, dn, do_ = dst_inf[(dx, dy)]
        new_flags = 0
        new_exit = new_npc = new_obj = None
        # Preserve destination exits
        if df & 1 and de:
            new_flags |= 1; new_exit = de
        # Copy source NPCs (fallback to dst)
        if sf & 2 and sn:
            new_flags |= 2; new_npc = sn; npcs += 1
        elif df & 2 and dn:
            new_flags |= 2; new_npc = dn
        # Copy source objects (fallback to dst)
        if sf & 4 and so:
            new_flags |= 4; new_obj = so; objs += 1
        elif df & 4 and do_:
            new_flags |= 4; new_obj = do_
        dst_inf[(dx, dy)] = (new_flags, new_exit, new_npc, new_obj)
    return npcs, objs


# ── Main ─────────────────────────────────────────────────────

def process_border(map_dir, border_name, src_map_num, dst_map_num,
                   tile_pairs, exit_configs, dry_run=False):
    """
    Process one border: copy tiles + fix exits.
    exit_configs: list of dicts with keys:
        map_num, clear_exits, set_exits
        clear_exits: list of (x, y) to remove exits
        set_exits: list of (x, y, dest_map, dest_x, dest_y) to add exits
    """
    print(f"\n{'='*60}")
    print(f"{border_name}: {len(tile_pairs)} tile copies")
    print(f"{'='*60}")

    # Load all needed map files
    maps_data = {}  # map_num -> (map_hdr, map_tiles, inf_hdr, inf_tiles)
    needed_maps = {src_map_num, dst_map_num}
    for cfg in exit_configs:
        needed_maps.add(cfg['map_num'])

    for mn in needed_maps:
        map_path = os.path.join(map_dir, f'Mapa{mn}.map')
        inf_path = os.path.join(map_dir, f'Mapa{mn}.inf')
        m_hdr, m_tiles = parse_all_map_tiles(map_path)
        has_inf = os.path.exists(inf_path) and os.path.getsize(inf_path) > HEADER_SIZE_INF
        i_hdr, i_tiles = parse_all_inf_tiles(inf_path) if has_inf else (None, None)
        maps_data[mn] = (m_hdr, m_tiles, i_hdr, i_tiles)

    # Copy tiles
    src_tiles = maps_data[src_map_num][1]
    dst_tiles = maps_data[dst_map_num][1]
    copied = copy_tiles(src_tiles, dst_tiles, tile_pairs)
    print(f"  Copied {copied} tiles (graphics)")

    # Copy inf tiles
    src_inf = maps_data[src_map_num][3]
    dst_inf = maps_data[dst_map_num][3]
    npcs, objs = copy_inf_tiles(src_inf, dst_inf, tile_pairs)
    print(f"  Copied NPCs: {npcs}, Objects: {objs}")

    # Fix exits
    for cfg in exit_configs:
        mn = cfg['map_num']
        _, m_tiles, i_hdr, i_tiles = maps_data[mn]
        if i_tiles is None:
            print(f"  [SKIP] Map{mn} has no .inf — cannot modify exits")
            continue

        # Clear old exits
        cleared = 0
        for x, y in cfg.get('clear_exits', []):
            ex = get_exit(i_tiles, x, y)
            if ex:
                clear_exit(i_tiles, x, y)
                cleared += 1

        # Set new exits (skip blocked tiles)
        added = 0
        skipped_blocked = 0
        for x, y, dm, dx, dy in cfg.get('set_exits', []):
            if is_blocked(m_tiles, x, y):
                skipped_blocked += 1
                continue
            set_exit(i_tiles, x, y, dm, dx, dy)
            added += 1

        print(f"  Map{mn} exits: cleared={cleared}, added={added}, skipped_blocked={skipped_blocked}")

    if dry_run:
        print("  [DRY RUN] Not writing files.")
        return

    # Write modified files
    for mn in needed_maps:
        m_hdr, m_tiles, i_hdr, i_tiles = maps_data[mn]

        # Always write .map (tiles may have been copied)
        map_path = os.path.join(map_dir, f'Mapa{mn}.map')
        bak = map_path + '.bak'
        if not os.path.exists(bak):
            shutil.copy2(map_path, bak)
        new_data = rebuild_map(m_hdr, m_tiles)
        with open(map_path, 'wb') as f:
            f.write(new_data)
        print(f"  Wrote Mapa{mn}.map ({len(new_data)} bytes)")

        # Write .inf if we have it
        if i_hdr and i_tiles:
            inf_path = os.path.join(map_dir, f'Mapa{mn}.inf')
            bak = inf_path + '.bak'
            if not os.path.exists(bak):
                shutil.copy2(inf_path, bak)
            new_data = rebuild_inf(i_hdr, i_tiles)
            with open(inf_path, 'wb') as f:
                f.write(new_data)
            print(f"  Wrote Mapa{mn}.inf ({len(new_data)} bytes)")


def make_row_pairs(src_rows, dst_rows):
    pairs = []
    for sr, dr in zip(src_rows, dst_rows):
        for x in range(1, MAP_SIZE + 1):
            pairs.append(((x, sr), (x, dr)))
    return pairs


def make_col_pairs(src_cols, dst_cols):
    pairs = []
    for sc, dc in zip(src_cols, dst_cols):
        for y in range(1, MAP_SIZE + 1):
            pairs.append(((sc, y), (dc, y)))
    return pairs


def run(map_dir, dry_run=False):
    print(f"Map directory: {map_dir}")

    # ── NORTH: Map28 <-> Map90 ──
    # Pattern (like Map28<->Map18):
    #   Map28 Y=7 -> Map90 Y=93 (arrive near south border)
    #   Map90 Y=94 -> Map28 Y=8 (return: 1 tile south, dest = 1 tile south of exit)
    # Tile copy: Map28 Y=8..15 -> Map90 Y=91..98 (8 rows = 6+2 extra)
    # X range for exits: match Map28's existing exit X range (12..91 from current data)

    north_tile_pairs = make_row_pairs(
        src_rows=range(8, 16),     # Map28 Y=8..15 (8 rows)
        dst_rows=range(91, 99),    # Map90 Y=91..98
    )

    # Build exit lists for Map28 and Map90
    # Map28: clear old Y=7 exits to Map90, set new Y=7 exits -> Map90 Y=93
    map28_north_clear = [(x, 7) for x in range(1, 101)]
    map28_north_set = [(x, 7, 90, x, 93) for x in range(12, 92)]  # X=12..91

    # Also clear Map28 Y=10 exits to Map90 (some exist at Y=10)
    map28_north_clear += [(x, 10) for x in range(1, 101)]

    # Map90: clear old Y=94 exits to Map28, set new Y=94 exits -> Map28 Y=8
    map90_clear = [(x, 94) for x in range(1, 101)]
    map90_set = [(x, 94, 28, x, 8) for x in range(12, 92)]

    north_exit_configs = [
        {'map_num': 28, 'clear_exits': map28_north_clear, 'set_exits': map28_north_set},
        {'map_num': 90, 'clear_exits': map90_clear, 'set_exits': map90_set},
    ]

    # ── WEST: Map28 <-> Map14 ──
    # Pattern:
    #   Map28 X=9 -> Map14 X=90 (arrive near east border)
    #   Map14 X=91 -> Map28 X=10 (return: 1 tile east, dest = 1 tile east of exit)
    # Tile copy: Map28 X=10..19 -> Map14 X=90..99 (10 cols = 8+2 extra)
    # Y range for exits: match current Y=12..93

    west_tile_pairs = make_col_pairs(
        src_cols=range(10, 20),    # Map28 X=10..19 (10 cols)
        dst_cols=range(90, 100),   # Map14 X=90..99
    )

    # Map28: update X=9 exits dest from Map14 X=89 to X=90
    map28_west_clear = [(9, y) for y in range(1, 101)]
    map28_west_set = [(9, y, 14, 90, y) for y in range(12, 94)]  # Y=12..93

    # Map14: move exits from X=92 to X=91, dest from Map28 X=12 to X=10
    map14_clear = [(92, y) for y in range(1, 101)]
    map14_set = [(91, y, 28, 10, y) for y in range(12, 94)]

    west_exit_configs = [
        {'map_num': 28, 'clear_exits': map28_west_clear, 'set_exits': map28_west_set},
        {'map_num': 14, 'clear_exits': map14_clear, 'set_exits': map14_set},
    ]

    # ── EAST: Map28 <-> Map54 ──
    # Pattern:
    #   Map28 X=92 -> Map54 X=11 (arrive near west border)
    #   Map54 X=10 -> Map28 X=91 (return: 1 tile west, dest = 1 tile west of exit)
    # Tile copy: Map28 X=82..91 -> Map54 X=2..11 (10 cols = 8+2 extra)
    # Y range for exits: match current Y=8..93

    east_tile_pairs = make_col_pairs(
        src_cols=range(82, 92),    # Map28 X=82..91 (10 cols)
        dst_cols=range(2, 12),     # Map54 X=2..11
    )

    # Map28: update X=92 exits dest from Map54 X=12 to X=11
    map28_east_clear = [(92, y) for y in range(1, 101)]
    map28_east_set = [(92, y, 54, 11, y) for y in range(8, 94)]  # Y=8..93

    # Map54: move exits from X=9 to X=10, dest from Map28 X=89 to X=91
    map54_clear = [(9, y) for y in range(1, 101)]
    map54_set = [(10, y, 28, 91, y) for y in range(8, 94)]

    east_exit_configs = [
        {'map_num': 28, 'clear_exits': map28_east_clear, 'set_exits': map28_east_set},
        {'map_num': 54, 'clear_exits': map54_clear, 'set_exits': map54_set},
    ]

    # Execute
    process_border(map_dir, "NORTH (Map28<->Map90)", 28, 90, north_tile_pairs, north_exit_configs, dry_run)
    process_border(map_dir, "WEST (Map28<->Map14)", 28, 14, west_tile_pairs, west_exit_configs, dry_run)
    process_border(map_dir, "EAST (Map28<->Map54)", 28, 54, east_tile_pairs, east_exit_configs, dry_run)


def main():
    dry_run = '--dry-run' in sys.argv

    if '--client' in sys.argv:
        run(CLIENT_MAP_DIR, dry_run)
    elif '--server' in sys.argv:
        run(SERVER_MAP_DIR, dry_run)
    else:
        # Both
        print("### SERVER ###")
        run(SERVER_MAP_DIR, dry_run)
        print("\n\n### CLIENT ###")
        run(CLIENT_MAP_DIR, dry_run)


if __name__ == '__main__':
    main()
