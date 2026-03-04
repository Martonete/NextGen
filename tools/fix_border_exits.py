#!/usr/bin/env python3
"""
Fix border tile exits + copy border tiles for seamless Map28 transitions.

Key insight: the visual border is between the EXIT row and the ARRIVAL row.
Tiles BEFORE the exit (in the departing map) must show the same graphics as
tiles BEFORE the exit (in the arriving map), so the viewport looks continuous.

Map28 adjacency (reference = Map28<->Map18 south):
  South: Map28 Y=94(exit) -> Map18 Y=8(arrive). Map18 Y=7(exit) -> Map28 Y=93(arrive)
  North: Map28 Y=7(exit) -> Map90 Y=93(arrive). Map90 Y=94(exit) -> Map28 Y=8(arrive)
  West:  Map28 X=9(exit) -> Map14 X=90(arrive). Map14 X=91(exit) -> Map28 X=10(arrive)
  East:  Map28 X=92(exit) -> Map54 X=11(arrive). Map54 X=10(exit) -> Map28 X=91(arrive)

Copy logic:
  For each border, the two maps share a seam. Copy tiles from each map into
  the other so that the zone around the seam looks identical from both sides.

  NORTH (seam between Map90 Y=94 and Map28 Y=8):
    Map28 Y=1..7  -> Map90 Y=87..93  (Map28 north zone appears in Map90 before exit)
    Map90 Y=94..100 -> Map28 Y=8..14 (Map90 south zone appears in Map28 after arrival)
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
    pos += 2
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
    return struct.pack('<hhh', dest_map, dest_x, dest_y)


def set_exit(inf_tiles, x, y, dest_map, dest_x, dest_y):
    flags, _, npc_data, obj_data = inf_tiles[(x, y)]
    inf_tiles[(x, y)] = (flags | 1, make_exit_data(dest_map, dest_x, dest_y), npc_data, obj_data)


def clear_exit(inf_tiles, x, y):
    flags, _, npc_data, obj_data = inf_tiles[(x, y)]
    inf_tiles[(x, y)] = (flags & ~1, None, npc_data, obj_data)


def get_exit(inf_tiles, x, y):
    flags, exit_data, _, _ = inf_tiles[(x, y)]
    if flags & 1 and exit_data:
        return struct.unpack('<hhh', exit_data)
    return None


# ── Tile copy helpers ────────────────────────────────────────

def strip_blocked_flag(tile_bytes):
    return bytes([tile_bytes[0] & ~1]) + tile_bytes[1:]


def set_blocked_flag(tile_bytes, blocked):
    if blocked:
        return bytes([tile_bytes[0] | 1]) + tile_bytes[1:]
    return bytes([tile_bytes[0] & ~1]) + tile_bytes[1:]


def copy_tiles(src_tiles, dst_tiles, tile_pairs):
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
    if src_inf is None or dst_inf is None:
        return 0, 0
    npcs = objs = 0
    for (sx, sy), (dx, dy) in tile_pairs:
        sf, se, sn, so = src_inf[(sx, sy)]
        df, de, dn, do_ = dst_inf[(dx, dy)]
        new_flags = 0; new_exit = new_npc = new_obj = None
        if df & 1 and de:  # preserve dest exits
            new_flags |= 1; new_exit = de
        if sf & 2 and sn:
            new_flags |= 2; new_npc = sn; npcs += 1
        elif df & 2 and dn:
            new_flags |= 2; new_npc = dn
        if sf & 4 and so:
            new_flags |= 4; new_obj = so; objs += 1
        elif df & 4 and do_:
            new_flags |= 4; new_obj = do_
        dst_inf[(dx, dy)] = (new_flags, new_exit, new_npc, new_obj)
    return npcs, objs


# ── Main processing ─────────────────────────────────────────

def process_all(map_dir, dry_run=False):
    print(f"Map directory: {map_dir}")

    # Load all needed maps
    map_nums = [28, 90, 14, 54]
    maps = {}  # num -> (m_hdr, m_tiles, i_hdr, i_tiles)
    for mn in map_nums:
        map_path = os.path.join(map_dir, f'Mapa{mn}.map')
        inf_path = os.path.join(map_dir, f'Mapa{mn}.inf')
        m_hdr, m_tiles = parse_all_map_tiles(map_path)
        has_inf = os.path.exists(inf_path) and os.path.getsize(inf_path) > HEADER_SIZE_INF
        i_hdr, i_tiles = parse_all_inf_tiles(inf_path) if has_inf else (None, None)
        maps[mn] = (m_hdr, m_tiles, i_hdr, i_tiles)

    VIEWPORT_H = 8   # ±6 Y tiles + 2 margin
    VIEWPORT_W = 10   # ±8 X tiles + 2 margin

    # ══════════════════════════════════════════════════════════
    # NORTH: Map28 <-> Map90
    # Seam: Map90 Y=94 (exit south) | Map28 Y=8 (arrival from north)
    # Also: Map28 Y=7 (exit north) | Map90 Y=93 (arrival from south)
    # ══════════════════════════════════════════════════════════
    print(f"\n{'='*60}")
    print("NORTH: Map28 <-> Map90")
    print(f"{'='*60}")

    m28_tiles = maps[28][1]
    m90_tiles = maps[90][1]

    # Copy Map28 north zone into Map90 south zone (before the exit at Y=94)
    # Map28 Y=1..VIEWPORT_H -> Map90 Y=(94-VIEWPORT_H)..(93)
    # Map28 Y=1 -> Map90 Y=86, ..., Map28 Y=8 -> Map90 Y=93
    pairs_28to90 = []
    for i in range(VIEWPORT_H):
        src_y = 1 + i           # Map28 Y=1..8
        dst_y = 94 - VIEWPORT_H + i  # Map90 Y=86..93
        for x in range(1, MAP_SIZE + 1):
            pairs_28to90.append(((x, src_y), (x, dst_y)))

    # Copy Map90 south zone into Map28 north zone (after arrival at Y=8)
    # Map90 Y=94..94+VIEWPORT_H-1 -> Map28 Y=8..8+VIEWPORT_H-1
    # Map90 Y=94 -> Map28 Y=8, ..., Map90 Y=101? capped at 100
    pairs_90to28 = []
    for i in range(VIEWPORT_H):
        src_y = 94 + i          # Map90 Y=94..101 (cap at 100)
        dst_y = 8 + i           # Map28 Y=8..15
        if src_y > MAP_SIZE:
            break
        for x in range(1, MAP_SIZE + 1):
            pairs_90to28.append(((x, src_y), (x, dst_y)))

    c1 = copy_tiles(m28_tiles, m90_tiles, pairs_28to90)
    c2 = copy_tiles(m90_tiles, m28_tiles, pairs_90to28)
    print(f"  Map28->Map90: {c1} tiles (Map28 Y=1..{VIEWPORT_H} -> Map90 Y={94-VIEWPORT_H}..93)")
    print(f"  Map90->Map28: {c2} tiles (Map90 Y=94..{min(94+VIEWPORT_H-1,100)} -> Map28 Y=8..{min(8+VIEWPORT_H-1,100)})")

    # Copy inf tiles
    m28_inf = maps[28][3]
    m90_inf = maps[90][3]
    n1, o1 = copy_inf_tiles(m28_inf, m90_inf, pairs_28to90) if m28_inf and m90_inf else (0, 0)
    n2, o2 = copy_inf_tiles(m90_inf, m28_inf, pairs_90to28) if m28_inf and m90_inf else (0, 0)

    # Fix exits (server only — client has no .inf)
    if m28_inf and m90_inf:
        # Clear old exits in both maps
        for x in range(1, 101):
            for y in range(1, 15):
                if get_exit(m28_inf, x, y): clear_exit(m28_inf, x, y)
            for y in range(86, 101):
                if get_exit(m90_inf, x, y): clear_exit(m90_inf, x, y)

        # Set new exits: Map28 Y=7 -> Map90 Y=93, Map90 Y=94 -> Map28 Y=8
        added28 = skipped28 = 0
        added90 = skipped90 = 0
        for x in range(12, 92):
            if is_blocked(m28_tiles, x, 7):
                skipped28 += 1
            else:
                set_exit(m28_inf, x, 7, 90, x, 93)
                added28 += 1
            if is_blocked(m90_tiles, x, 94):
                skipped90 += 1
            else:
                set_exit(m90_inf, x, 94, 28, x, 8)
                added90 += 1
        print(f"  Exits Map28 Y=7->Map90 Y=93: {added28} added, {skipped28} skipped (blocked)")
        print(f"  Exits Map90 Y=94->Map28 Y=8: {added90} added, {skipped90} skipped (blocked)")

    # ══════════════════════════════════════════════════════════
    # WEST: Map28 <-> Map14
    # Seam: Map14 X=91 (exit east) | Map28 X=10 (arrival from west)
    # Also: Map28 X=9 (exit west) | Map14 X=90 (arrival from east)
    # ══════════════════════════════════════════════════════════
    print(f"\n{'='*60}")
    print("WEST: Map28 <-> Map14")
    print(f"{'='*60}")

    m14_tiles = maps[14][1]

    # Copy Map28 west zone into Map14 east zone (before exit at X=91)
    # Map28 X=1..VIEWPORT_W -> Map14 X=(91-VIEWPORT_W)..90
    pairs_28to14 = []
    for i in range(VIEWPORT_W):
        src_x = 1 + i
        dst_x = 91 - VIEWPORT_W + i
        for y in range(1, MAP_SIZE + 1):
            pairs_28to14.append(((src_x, y), (dst_x, y)))

    # Copy Map14 east zone into Map28 west zone (after arrival at X=10)
    # Map14 X=91..91+VIEWPORT_W-1 -> Map28 X=10..10+VIEWPORT_W-1
    pairs_14to28 = []
    for i in range(VIEWPORT_W):
        src_x = 91 + i
        dst_x = 10 + i
        if src_x > MAP_SIZE:
            break
        for y in range(1, MAP_SIZE + 1):
            pairs_14to28.append(((src_x, y), (dst_x, y)))

    c1 = copy_tiles(m28_tiles, m14_tiles, pairs_28to14)
    c2 = copy_tiles(m14_tiles, m28_tiles, pairs_14to28)
    print(f"  Map28->Map14: {c1} tiles (Map28 X=1..{VIEWPORT_W} -> Map14 X={91-VIEWPORT_W}..90)")
    print(f"  Map14->Map28: {c2} tiles (Map14 X=91..{min(91+VIEWPORT_W-1,100)} -> Map28 X=10..{min(10+VIEWPORT_W-1,100)})")

    m14_inf = maps[14][3]
    n1, o1 = copy_inf_tiles(m28_inf, m14_inf, pairs_28to14) if m28_inf and m14_inf else (0, 0)
    n2, o2 = copy_inf_tiles(m14_inf, m28_inf, pairs_14to28) if m28_inf and m14_inf else (0, 0)

    if m28_inf and m14_inf:
        for y in range(1, 101):
            for x in range(1, 20):
                if get_exit(m28_inf, x, y): clear_exit(m28_inf, x, y)
            for x in range(81, 101):
                if get_exit(m14_inf, x, y): clear_exit(m14_inf, x, y)

        added28 = skipped28 = 0
        added14 = skipped14 = 0
        for y in range(12, 94):
            if is_blocked(m28_tiles, 9, y):
                skipped28 += 1
            else:
                set_exit(m28_inf, 9, y, 14, 90, y)
                added28 += 1
            if is_blocked(m14_tiles, 91, y):
                skipped14 += 1
            else:
                set_exit(m14_inf, 91, y, 28, 10, y)
                added14 += 1
        print(f"  Exits Map28 X=9->Map14 X=90: {added28} added, {skipped28} skipped")
        print(f"  Exits Map14 X=91->Map28 X=10: {added14} added, {skipped14} skipped")

    # ══════════════════════════════════════════════════════════
    # EAST: Map28 <-> Map54
    # Seam: Map54 X=10 (exit west) | Map28 X=91 (arrival from east)
    # Also: Map28 X=92 (exit east) | Map54 X=11 (arrival from west)
    # ══════════════════════════════════════════════════════════
    print(f"\n{'='*60}")
    print("EAST: Map28 <-> Map54")
    print(f"{'='*60}")

    m54_tiles = maps[54][1]

    # Copy Map28 east zone into Map54 west zone (before exit at X=10)
    # Map28 X=(100-VIEWPORT_W+1)..100 -> Map54 X=(10-VIEWPORT_W)..9
    # But dest X must be >= 1
    pairs_28to54 = []
    for i in range(VIEWPORT_W):
        src_x = MAP_SIZE - VIEWPORT_W + 1 + i  # Map28 X=91..100
        dst_x = 10 - VIEWPORT_W + i             # Map54 X=0..9 -> cap at 1
        if dst_x < 1:
            continue
        for y in range(1, MAP_SIZE + 1):
            pairs_28to54.append(((src_x, y), (dst_x, y)))

    # Copy Map54 west zone into Map28 east zone (after arrival at X=91)
    # Map54 X=10..10+VIEWPORT_W-1 -> Map28 X=91..91+VIEWPORT_W-1
    pairs_54to28 = []
    for i in range(VIEWPORT_W):
        src_x = 10 + i   # Map54 X=10..19
        dst_x = 91 + i   # Map28 X=91..100
        if dst_x > MAP_SIZE:
            break
        for y in range(1, MAP_SIZE + 1):
            pairs_54to28.append(((src_x, y), (dst_x, y)))

    c1 = copy_tiles(m28_tiles, m54_tiles, pairs_28to54)
    c2 = copy_tiles(m54_tiles, m28_tiles, pairs_54to28)
    print(f"  Map28->Map54: {c1} tiles")
    print(f"  Map54->Map28: {c2} tiles")

    m54_inf = maps[54][3]
    n1, o1 = copy_inf_tiles(m28_inf, m54_inf, pairs_28to54) if m28_inf and m54_inf else (0, 0)
    n2, o2 = copy_inf_tiles(m54_inf, m28_inf, pairs_54to28) if m28_inf and m54_inf else (0, 0)

    if m28_inf and m54_inf:
        for y in range(1, 101):
            for x in range(82, 101):
                if get_exit(m28_inf, x, y): clear_exit(m28_inf, x, y)
            for x in range(1, 20):
                if get_exit(m54_inf, x, y): clear_exit(m54_inf, x, y)

        added28 = skipped28 = 0
        added54 = skipped54 = 0
        for y in range(8, 94):
            if is_blocked(m28_tiles, 92, y):
                skipped28 += 1
            else:
                set_exit(m28_inf, 92, y, 54, 11, y)
                added28 += 1
            if is_blocked(m54_tiles, 10, y):
                skipped54 += 1
            else:
                set_exit(m54_inf, 10, y, 28, 91, y)
                added54 += 1
        print(f"  Exits Map28 X=92->Map54 X=11: {added28} added, {skipped28} skipped")
        print(f"  Exits Map54 X=10->Map28 X=91: {added54} added, {skipped54} skipped")

    # ── Write all modified files ──
    if dry_run:
        print("\n[DRY RUN] Not writing files.")
        return

    print("\nWriting files...")
    for mn in map_nums:
        m_hdr, m_tiles, i_hdr, i_tiles = maps[mn]
        map_path = os.path.join(map_dir, f'Mapa{mn}.map')
        bak = map_path + '.bak2'
        if not os.path.exists(bak):
            shutil.copy2(map_path, bak)
        data = rebuild_map(m_hdr, m_tiles)
        with open(map_path, 'wb') as f:
            f.write(data)
        print(f"  Mapa{mn}.map ({len(data)} bytes)")

        if i_hdr and i_tiles:
            inf_path = os.path.join(map_dir, f'Mapa{mn}.inf')
            bak = inf_path + '.bak2'
            if not os.path.exists(bak):
                shutil.copy2(inf_path, bak)
            data = rebuild_inf(i_hdr, i_tiles)
            with open(inf_path, 'wb') as f:
                f.write(data)
            print(f"  Mapa{mn}.inf ({len(data)} bytes)")


def main():
    dry_run = '--dry-run' in sys.argv
    if '--client' in sys.argv:
        process_all(CLIENT_MAP_DIR, dry_run)
    elif '--server' in sys.argv:
        process_all(SERVER_MAP_DIR, dry_run)
    else:
        print("### SERVER ###")
        process_all(SERVER_MAP_DIR, dry_run)
        print("\n\n### CLIENT ###")
        process_all(CLIENT_MAP_DIR, dry_run)


if __name__ == '__main__':
    main()
