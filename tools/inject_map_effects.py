#!/usr/bin/env python3
"""
Parses MapEffects.cs to extract hardcoded particle/light data per map,
then injects that data into the .map binary files (byFlags bits 5/6).

.map tile format (variable-length per tile):
  byte  byFlags
  int16 Layer1        (always)
  int16 Layer2        (if byFlags & 2)
  int16 Layer3        (if byFlags & 4)
  int16 Layer4        (if byFlags & 8)
  int16 Trigger       (if byFlags & 16)
  int16 ParticleGroup (if byFlags & 32)
  int16 LightRange    (if byFlags & 64)
  int16 LightR        (if byFlags & 64)
  int16 LightG        (if byFlags & 64)
  int16 LightB        (if byFlags & 64)

All int16 are little-endian signed.
Header: 273 bytes (skipped).
Tiles: y=1..100, x=1..100.
"""

import re
import struct
import sys
import os
from pathlib import Path
from collections import defaultdict
from copy import deepcopy

HEADER_SIZE = 273
MAP_W = 100
MAP_H = 100


def parse_map_effects(cs_path: str):
    """Parse MapEffects.cs and return {map_number: {'particles': [(def,x,y),...], 'lights': [(x,y,range,r,g,b),...]}}"""
    with open(cs_path, 'r') as f:
        text = f.read()

    # Find which SetupMapN function is called for each map number
    # Pattern: case N: SetupMapN(state); break;
    # Also handle: case N: SetupArenaMap(s); break; via delegation

    # First, find the switch block to map case numbers to function names
    case_pattern = re.compile(r'case\s+(\d+):\s+Setup(\w+)\(', re.MULTILINE)
    map_to_func = {}
    for m in case_pattern.finditer(text):
        map_num = int(m.group(1))
        func_name = f"Setup{m.group(2)}"
        map_to_func[map_num] = func_name

    # Parse each function body for P() and L() calls
    func_pattern = re.compile(
        r'private\s+static\s+void\s+(Setup\w+)\(GameState\s+s\)\s*(?:=>.*?;|\{(.*?)\})',
        re.DOTALL
    )

    func_bodies = {}
    for m in func_pattern.finditer(text):
        func_name = m.group(1)
        body = m.group(2) or m.group(0)  # arrow expr or block
        func_bodies[func_name] = body

    # Handle delegation: SetupMap132(s) => SetupArenaMap(s);
    deleg_pattern = re.compile(
        r'private\s+static\s+void\s+(Setup\w+)\(GameState\s+s\)\s*=>\s*(Setup\w+)\(s\);'
    )
    delegations = {}
    for m in deleg_pattern.finditer(text):
        delegations[m.group(1)] = m.group(2)

    p_pattern = re.compile(r'P\(s,(\d+),(\d+),(\d+)\)')
    l_pattern = re.compile(r'L\(s,(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)')

    result = {}
    for map_num, func_name in map_to_func.items():
        # Resolve delegation
        actual_func = func_name
        while actual_func in delegations:
            actual_func = delegations[actual_func]

        body = func_bodies.get(actual_func, "")

        particles = []
        for m in p_pattern.finditer(body):
            defn, x, y = int(m.group(1)), int(m.group(2)), int(m.group(3))
            particles.append((defn, x, y))

        lights = []
        for m in l_pattern.finditer(body):
            x, y, rng, r, g, b = (int(m.group(i)) for i in range(1, 7))
            lights.append((x, y, rng, r, g, b))

        result[map_num] = {'particles': particles, 'lights': lights}

    return result


def read_map_tiles(data: bytes):
    """Parse .map binary into list of tile dicts, preserving all fields."""
    tiles = []  # 10000 tiles, indexed [y*100+x] for y=0..99, x=0..99
    pos = HEADER_SIZE

    for y in range(MAP_H):
        for x in range(MAP_W):
            if pos >= len(data):
                # Pad remaining tiles
                tiles.append({'byFlags': 0, 'layer1': 0, 'layer2': 0, 'layer3': 0,
                              'layer4': 0, 'trigger': 0, 'particle': 0,
                              'light_range': 0, 'light_r': 0, 'light_g': 0, 'light_b': 0})
                continue

            byFlags = data[pos]; pos += 1
            layer1 = struct.unpack_from('<h', data, pos)[0]; pos += 2

            layer2 = 0
            if byFlags & 2:
                layer2 = struct.unpack_from('<h', data, pos)[0]; pos += 2

            layer3 = 0
            if byFlags & 4:
                layer3 = struct.unpack_from('<h', data, pos)[0]; pos += 2

            layer4 = 0
            if byFlags & 8:
                layer4 = struct.unpack_from('<h', data, pos)[0]; pos += 2

            trigger = 0
            if byFlags & 16:
                trigger = struct.unpack_from('<h', data, pos)[0]; pos += 2

            particle = 0
            if byFlags & 32:
                particle = struct.unpack_from('<h', data, pos)[0]; pos += 2

            light_range = 0; light_r = 0; light_g = 0; light_b = 0
            if byFlags & 64:
                light_range = struct.unpack_from('<h', data, pos)[0]; pos += 2
                light_r = struct.unpack_from('<h', data, pos)[0]; pos += 2
                light_g = struct.unpack_from('<h', data, pos)[0]; pos += 2
                light_b = struct.unpack_from('<h', data, pos)[0]; pos += 2

            tiles.append({
                'byFlags': byFlags,
                'layer1': layer1, 'layer2': layer2, 'layer3': layer3, 'layer4': layer4,
                'trigger': trigger, 'particle': particle,
                'light_range': light_range, 'light_r': light_r, 'light_g': light_g, 'light_b': light_b
            })

    return tiles


def write_map_tiles(header: bytes, tiles: list) -> bytes:
    """Serialize tiles back to .map binary format."""
    parts = [header]

    for tile in tiles:
        # Recompute byFlags from actual data
        flags = tile['byFlags'] & 1  # preserve blocked bit
        if tile['layer2'] != 0: flags |= 2
        if tile['layer3'] != 0: flags |= 4
        if tile['layer4'] != 0: flags |= 8
        if tile['trigger'] != 0: flags |= 16
        if tile['particle'] != 0: flags |= 32
        if tile['light_range'] != 0: flags |= 64

        parts.append(struct.pack('B', flags))
        parts.append(struct.pack('<h', tile['layer1']))
        if flags & 2:
            parts.append(struct.pack('<h', tile['layer2']))
        if flags & 4:
            parts.append(struct.pack('<h', tile['layer3']))
        if flags & 8:
            parts.append(struct.pack('<h', tile['layer4']))
        if flags & 16:
            parts.append(struct.pack('<h', tile['trigger']))
        if flags & 32:
            parts.append(struct.pack('<h', tile['particle']))
        if flags & 64:
            parts.append(struct.pack('<h', tile['light_range']))
            parts.append(struct.pack('<h', tile['light_r']))
            parts.append(struct.pack('<h', tile['light_g']))
            parts.append(struct.pack('<h', tile['light_b']))

    return b''.join(parts)


def inject_effects(map_dir: str, effects: dict, dry_run: bool = False):
    """Inject particle/light data from MapEffects into .map files."""
    stats = {'maps_modified': 0, 'particles_injected': 0, 'lights_injected': 0,
             'conflicts_particle': 0, 'conflicts_light': 0}

    for map_num, data in sorted(effects.items()):
        map_path = os.path.join(map_dir, f"Mapa{map_num}.map")
        if not os.path.exists(map_path):
            print(f"  SKIP Mapa{map_num}.map — file not found")
            continue

        with open(map_path, 'rb') as f:
            raw = f.read()

        header = raw[:HEADER_SIZE]
        tiles = read_map_tiles(raw)
        modified = False

        # Inject particles (last-write-wins for same tile, matching VB6 behavior)
        for defn, x, y in data['particles']:
            if x < 1 or x > 100 or y < 1 or y > 100:
                print(f"  WARN Map{map_num}: particle at ({x},{y}) out of range")
                continue
            idx = (y - 1) * 100 + (x - 1)
            tile = tiles[idx]
            if tile['particle'] != 0 and tile['particle'] != defn:
                print(f"  CONFLICT Map{map_num} tile({x},{y}): existing particle={tile['particle']}, overwriting with={defn} (last-write-wins)")
                stats['conflicts_particle'] += 1
            tile['particle'] = defn
            modified = True
            stats['particles_injected'] += 1

        # Inject lights (last-write-wins for same tile)
        for x, y, rng, r, g, b in data['lights']:
            if x < 1 or x > 100 or y < 1 or y > 100:
                print(f"  WARN Map{map_num}: light at ({x},{y}) out of range")
                continue
            idx = (y - 1) * 100 + (x - 1)
            tile = tiles[idx]
            if tile['light_range'] != 0 and (tile['light_range'] != rng or tile['light_r'] != r):
                print(f"  CONFLICT Map{map_num} tile({x},{y}): existing light range={tile['light_range']}, overwriting with={rng}")
                stats['conflicts_light'] += 1
            tile['light_range'] = rng
            tile['light_r'] = r
            tile['light_g'] = g
            tile['light_b'] = b
            modified = True
            stats['lights_injected'] += 1

        if modified:
            new_data = write_map_tiles(header, tiles)

            # Verify round-trip: build expected final state (last-write-wins) then check
            expected_particles = {}  # (x,y) -> defn
            for defn, x, y in data['particles']:
                if 1 <= x <= 100 and 1 <= y <= 100:
                    expected_particles[(x, y)] = defn
            expected_lights = {}  # (x,y) -> (rng, r, g, b)
            for x, y, rng, r, g, b in data['lights']:
                if 1 <= x <= 100 and 1 <= y <= 100:
                    expected_lights[(x, y)] = (rng, r, g, b)

            verify_tiles = read_map_tiles(new_data)
            for (x, y), defn in expected_particles.items():
                idx = (y - 1) * 100 + (x - 1)
                assert verify_tiles[idx]['particle'] == defn, \
                    f"Verify FAIL Map{map_num} tile({x},{y}): particle expected {defn}, got {verify_tiles[idx]['particle']}"
            for (x, y), (rng, r, g, b) in expected_lights.items():
                idx = (y - 1) * 100 + (x - 1)
                vt = verify_tiles[idx]
                assert vt['light_range'] == rng, \
                    f"Verify FAIL Map{map_num} tile({x},{y}): range expected {rng}, got {vt['light_range']}"

            if not dry_run:
                with open(map_path, 'wb') as f:
                    f.write(new_data)
                print(f"  OK Mapa{map_num}.map — {len(data['particles'])}P + {len(data['lights'])}L injected ({len(raw)}→{len(new_data)} bytes)")
            else:
                print(f"  DRY Mapa{map_num}.map — {len(data['particles'])}P + {len(data['lights'])}L would be injected ({len(raw)}→{len(new_data)} bytes)")

            stats['maps_modified'] += 1

    return stats


def main():
    dry_run = '--dry-run' in sys.argv

    base = Path(__file__).resolve().parent.parent  # server-rust/
    cs_path = base / "client" / "Scripts" / "Game" / "MapEffects.cs"
    map_dir = base / "client" / "Data" / "Maps"

    if not cs_path.exists():
        print(f"ERROR: {cs_path} not found")
        sys.exit(1)
    if not map_dir.exists():
        print(f"ERROR: {map_dir} not found")
        sys.exit(1)

    print(f"Parsing {cs_path}...")
    effects = parse_map_effects(str(cs_path))

    total_p = sum(len(d['particles']) for d in effects.values())
    total_l = sum(len(d['lights']) for d in effects.values())
    print(f"Found {len(effects)} maps with {total_p} particles + {total_l} lights")
    print()

    mode = "DRY RUN" if dry_run else "INJECTING"
    print(f"=== {mode} into {map_dir} ===")
    stats = inject_effects(str(map_dir), effects, dry_run=dry_run)

    print()
    print(f"Done: {stats['maps_modified']} maps modified, "
          f"{stats['particles_injected']} particles + {stats['lights_injected']} lights injected, "
          f"{stats['conflicts_particle']} particle conflicts, {stats['conflicts_light']} light conflicts")


if __name__ == '__main__':
    main()
