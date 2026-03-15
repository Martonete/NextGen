#!/usr/bin/env python3
"""Generate a 1000x1000 .aomap from existing Mapa1.map (100x100).
Places the original map centered at (450,450)-(549,549) so the center
of the original map lands around (500,500). Fills the rest with grass."""

import struct
import sys
import os
import random

MAP_W = 1000
MAP_H = 1000
LEGACY_W = 100
LEGACY_H = 100

# Grass tile GRHs (6000-6015, cycling pattern like the original)
GRASS_GRHS = list(range(6000, 6016))

# Position to place the original 100x100 within the 1000x1000
OFFSET_X = 450  # original tile (0,0) maps to (450,450)
OFFSET_Y = 450


def read_legacy_map(path):
    """Read a legacy 100x100 .map file. Returns list of tile dicts."""
    with open(path, 'rb') as f:
        f.read(273)  # skip header
        tiles = []
        for y in range(LEGACY_H):
            for x in range(LEGACY_W):
                raw_flags = struct.unpack('B', f.read(1))[0]
                g = [0, 0, 0, 0]
                g[0] = struct.unpack('<h', f.read(2))[0]
                if raw_flags & 0x02:
                    g[1] = struct.unpack('<h', f.read(2))[0]
                if raw_flags & 0x04:
                    g[2] = struct.unpack('<h', f.read(2))[0]
                if raw_flags & 0x08:
                    g[3] = struct.unpack('<h', f.read(2))[0]
                trigger = 0
                if raw_flags & 0x10:
                    trigger = struct.unpack('<h', f.read(2))[0]
                particle = 0
                if raw_flags & 0x20:
                    particle = struct.unpack('<h', f.read(2))[0]
                light = None
                if raw_flags & 0x40:
                    light = struct.unpack('<hhhh', f.read(8))

                blocked = bool(raw_flags & 0x01)
                tiles.append({
                    'blocked': blocked,
                    'graphic': g,
                    'trigger': trigger,
                    'particle': particle,
                    'light': light,
                })
        return tiles


def read_legacy_inf(path):
    """Read a legacy 100x100 .inf file. Returns list of tile dicts."""
    with open(path, 'rb') as f:
        f.read(10)  # skip header
        tiles = []
        for y in range(LEGACY_H):
            for x in range(LEGACY_W):
                raw_flags = struct.unpack('B', f.read(1))[0]
                exit_data = None
                if raw_flags & 0x01:
                    exit_data = struct.unpack('<hhh', f.read(6))
                npc = 0
                if raw_flags & 0x02:
                    npc = struct.unpack('<h', f.read(2))[0]
                obj_index = 0
                obj_amount = 0
                if raw_flags & 0x04:
                    obj_index = struct.unpack('<h', f.read(2))[0]
                    obj_amount = struct.unpack('<h', f.read(2))[0]
                tiles.append({
                    'exit': exit_data,
                    'npc': npc,
                    'obj_index': obj_index,
                    'obj_amount': obj_amount,
                })
        return tiles


def write_tile_bytes(tile):
    """Encode a single tile in ByFlags format (same as .map)."""
    flags = 0
    if tile['blocked']:
        flags |= 0x01
    if tile['graphic'][1] != 0:
        flags |= 0x02
    if tile['graphic'][2] != 0:
        flags |= 0x04
    if tile['graphic'][3] != 0:
        flags |= 0x08
    if tile['trigger'] != 0:
        flags |= 0x10
    if tile['particle'] != 0:
        flags |= 0x20
    if tile['light'] is not None:
        flags |= 0x40

    data = struct.pack('B', flags)
    data += struct.pack('<h', tile['graphic'][0])
    if flags & 0x02:
        data += struct.pack('<h', tile['graphic'][1])
    if flags & 0x04:
        data += struct.pack('<h', tile['graphic'][2])
    if flags & 0x08:
        data += struct.pack('<h', tile['graphic'][3])
    if flags & 0x10:
        data += struct.pack('<h', tile['trigger'])
    if flags & 0x20:
        data += struct.pack('<h', tile['particle'])
    if flags & 0x40:
        data += struct.pack('<hhhh', *tile['light'])
    return data


def write_inf_tile_bytes(tile):
    """Encode a single .inf tile in ByFlags format."""
    flags = 0
    if tile['exit'] is not None:
        flags |= 0x01
    if tile['npc'] != 0:
        flags |= 0x02
    if tile['obj_index'] != 0:
        flags |= 0x04

    data = struct.pack('B', flags)
    if flags & 0x01:
        data += struct.pack('<hhh', *tile['exit'])
    if flags & 0x02:
        data += struct.pack('<h', tile['npc'])
    if flags & 0x04:
        data += struct.pack('<h', tile['obj_index'])
        data += struct.pack('<h', tile['obj_amount'])
    return data


def make_grass_tile(x, y):
    """Create a grass tile with cycling GRH pattern."""
    grh = GRASS_GRHS[(x + y * 4) % len(GRASS_GRHS)]
    return {
        'blocked': False,
        'graphic': [grh, 0, 0, 0],
        'trigger': 0,
        'particle': 0,
        'light': None,
    }


def make_empty_inf():
    return {'exit': None, 'npc': 0, 'obj_index': 0, 'obj_amount': 0}


def main():
    base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    maps_dir = os.path.join(base, 'server', 'maps')

    map_path = os.path.join(maps_dir, 'Mapa1.map')
    inf_path = os.path.join(maps_dir, 'Mapa1.inf')

    if not os.path.exists(map_path):
        print(f"ERROR: {map_path} not found")
        sys.exit(1)

    print(f"Reading legacy Mapa1.map ({LEGACY_W}x{LEGACY_H})...")
    legacy_tiles = read_legacy_map(map_path)

    print(f"Reading legacy Mapa1.inf...")
    legacy_inf = read_legacy_inf(inf_path)

    print(f"Generating {MAP_W}x{MAP_H} .aomap...")

    # Write .aomap (map tile data)
    aomap_path = os.path.join(maps_dir, 'Mapa1.aomap')
    with open(aomap_path, 'wb') as f:
        # Header: 16 bytes
        f.write(b'AOMAP\x00')                    # Magic (6 bytes)
        f.write(struct.pack('<H', 1))             # Version (2 bytes)
        f.write(struct.pack('<H', MAP_W))         # Width (2 bytes)
        f.write(struct.pack('<H', MAP_H))         # Height (2 bytes)
        f.write(struct.pack('<I', 0))             # Flags (4 bytes)

        # Tiles: Y then X (row-major, matching legacy format)
        count = 0
        for y in range(MAP_H):
            for x in range(MAP_W):
                # Check if this position falls within the original 100x100
                ox = x - OFFSET_X
                oy = y - OFFSET_Y
                if 0 <= ox < LEGACY_W and 0 <= oy < LEGACY_H:
                    tile = legacy_tiles[oy * LEGACY_W + ox]
                else:
                    tile = make_grass_tile(x, y)
                f.write(write_tile_bytes(tile))
                count += 1

        print(f"  Wrote {count} tiles to {aomap_path}")

    # Write .aoinf (extended inf data, same header format)
    aoinf_path = os.path.join(maps_dir, 'Mapa1.aoinf')
    with open(aoinf_path, 'wb') as f:
        # Same header as .aomap
        f.write(b'AOINF\x00')                    # Magic (6 bytes)
        f.write(struct.pack('<H', 1))             # Version (2 bytes)
        f.write(struct.pack('<H', MAP_W))         # Width (2 bytes)
        f.write(struct.pack('<H', MAP_H))         # Height (2 bytes)
        f.write(struct.pack('<I', 0))             # Flags (4 bytes)

        count = 0
        for y in range(MAP_H):
            for x in range(MAP_W):
                ox = x - OFFSET_X
                oy = y - OFFSET_Y
                if 0 <= ox < LEGACY_W and 0 <= oy < LEGACY_H:
                    inf = legacy_inf[oy * LEGACY_W + ox]
                else:
                    inf = make_empty_inf()
                f.write(write_inf_tile_bytes(inf))
                count += 1

        print(f"  Wrote {count} inf tiles to {aoinf_path}")

    # File sizes
    map_size = os.path.getsize(aomap_path)
    inf_size = os.path.getsize(aoinf_path)
    print(f"\nGenerated files:")
    print(f"  {aomap_path}: {map_size:,} bytes ({map_size/1024/1024:.1f} MB)")
    print(f"  {aoinf_path}: {inf_size:,} bytes ({inf_size/1024/1024:.1f} MB)")
    print(f"\nOriginal 100x100 placed at offset ({OFFSET_X},{OFFSET_Y})")
    print(f"Center of original map: ({OFFSET_X+50},{OFFSET_Y+50}) = (500,500)")


if __name__ == '__main__':
    main()
