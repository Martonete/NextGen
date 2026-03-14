#!/usr/bin/env python3
"""
Swap water textures from 13.3-argentum into TSAO client.
Reads both Graficos.ind files, finds water frame sub-GRHs,
copies pixel regions from 13.3 source image onto TSAO target image.

Water GRH range: 1505-1520 (Layer1 water animations, Layer2=0).
These are multi-frame animations whose frames reference single-frame GRHs
that contain the actual FileNum, sX, sY, width, height.

Revertibility: git checkout the modified files.
"""
import struct
import sys
import os
from pathlib import Path

# VB6 binary format:
# Header: fileVersion(i32 LE), grhCount(i32 LE)
# Entries (loop until EOF):
#   GrhIndex(i32 LE), numFrames(i16 LE)
#   if numFrames == 1: FileNum(i32 LE), sX(i16), sY(i16), pixelWidth(i16), pixelHeight(i16)
#   if numFrames > 1:  Frames[numFrames] each i32 LE, Speed(f32 LE)

def parse_graficos_ind(path):
    """Parse Graficos.ind and return dict of GrhData entries."""
    grh_data = {}
    with open(path, 'rb') as f:
        file_version, grh_count = struct.unpack('<ii', f.read(8))
        print(f"  Version: {file_version}, GRH count: {grh_count}")

        while True:
            buf = f.read(6)  # GrhIndex(4) + numFrames(2)
            if len(buf) < 6:
                break
            grh_index, num_frames = struct.unpack('<ih', buf)

            entry = {'index': grh_index, 'numFrames': num_frames}

            if num_frames > 1:
                # Animation: read frame indices (each i32) + speed (f32)
                frames_buf = f.read(num_frames * 4 + 4)
                frames = list(struct.unpack(f'<{num_frames}i', frames_buf[:num_frames*4]))
                speed = struct.unpack('<f', frames_buf[num_frames*4:])[0]
                entry['frames'] = frames
                entry['speed'] = speed
            elif num_frames == 1:
                # Static: FileNum(i32), sX(i16), sY(i16), pixelWidth(i16), pixelHeight(i16)
                static_buf = f.read(12)  # 4 + 2 + 2 + 2 + 2 = 12
                file_num, sx, sy, pw, ph = struct.unpack('<ihhhh', static_buf)
                entry['fileNum'] = file_num
                entry['sX'] = sx
                entry['sY'] = sy
                entry['pixelWidth'] = pw
                entry['pixelHeight'] = ph
            else:
                print(f"  WARNING: GRH {grh_index} has numFrames={num_frames}, skipping")
                continue

            grh_data[grh_index] = entry

    print(f"  Parsed {len(grh_data)} GRH entries")
    return grh_data


def get_water_frame_grhs(grh_data, water_range=(1505, 1520)):
    """Get all single-frame sub-GRHs referenced by water animation GRHs."""
    frame_grhs = set()
    for grh_id in range(water_range[0], water_range[1] + 1):
        if grh_id not in grh_data:
            print(f"  WARNING: Water GRH {grh_id} not found")
            continue
        entry = grh_data[grh_id]
        if entry['numFrames'] > 1:
            print(f"  GRH {grh_id}: {entry['numFrames']} frames → {entry['frames']}")
            for frame_id in entry['frames']:
                frame_grhs.add(frame_id)
        else:
            print(f"  GRH {grh_id}: static (FileNum={entry.get('fileNum')})")
            frame_grhs.add(grh_id)
    return frame_grhs


def print_frame_details(grh_data, frame_grhs, label):
    """Print details of frame GRHs grouped by FileNum."""
    by_file = {}
    for fid in sorted(frame_grhs):
        if fid not in grh_data:
            print(f"  WARNING: Frame GRH {fid} not found in {label}")
            continue
        e = grh_data[fid]
        if e['numFrames'] != 1:
            print(f"  WARNING: Frame GRH {fid} is animation, not static")
            continue
        fn = e['fileNum']
        if fn not in by_file:
            by_file[fn] = []
        by_file[fn].append(e)
        print(f"  GRH {fid}: FileNum={fn}, ({e['sX']},{e['sY']}) {e['pixelWidth']}x{e['pixelHeight']}")
    return by_file


def main():
    base = Path('/workspace/Tierras-Sagradas-AO')
    tsao_ind = base / 'server-rust/client/Data/INIT/Graficos.ind'
    ao13_ind = base / '13.3-argentum/client/INIT/Graficos.ind'

    print("=== Parsing TSAO Graficos.ind ===")
    tsao_grh = parse_graficos_ind(tsao_ind)

    print("\n=== Parsing 13.3 Graficos.ind ===")
    ao13_grh = parse_graficos_ind(ao13_ind)

    print("\n=== TSAO Water GRHs (1505-1520) ===")
    tsao_frames = get_water_frame_grhs(tsao_grh)
    print(f"\nTSAO water frame GRHs: {sorted(tsao_frames)}")
    print("\n--- TSAO frame details ---")
    tsao_by_file = print_frame_details(tsao_grh, tsao_frames, "TSAO")

    print("\n=== 13.3 Water GRHs (1505-1520) ===")
    ao13_frames = get_water_frame_grhs(ao13_grh)
    print(f"\n13.3 water frame GRHs: {sorted(ao13_frames)}")
    print("\n--- 13.3 frame details ---")
    ao13_by_file = print_frame_details(ao13_grh, ao13_frames, "13.3")

    # Now attempt pixel copy
    try:
        from PIL import Image
    except ImportError:
        print("\n[!] Pillow not installed. Install with: pip install Pillow")
        print("    Then re-run this script to perform the actual pixel copy.")
        return

    if not tsao_by_file or not ao13_by_file:
        print("\nNo file mappings found, cannot copy pixels.")
        return

    # For each water frame, copy pixels from 13.3 source → TSAO target
    # Both use the same GRH IDs (1505-1520 → same frame sub-GRH IDs)
    # but different FileNum (TSAO=20, 13.3=12039)
    tsao_textures_dir = base / 'server-rust/client/Data/Graficos'
    ao13_textures_dir = base / '13.3-argentum/client/Graficos'

    # Load source images (13.3)
    ao13_images = {}
    for fn in ao13_by_file:
        # Try various extensions
        for ext in ['.png', '.bmp', '.BMP', '.PNG']:
            img_path = ao13_textures_dir / f"{fn}{ext}"
            if img_path.exists():
                ao13_images[fn] = Image.open(img_path).convert('RGBA')
                print(f"\nLoaded 13.3 texture: {img_path} ({ao13_images[fn].size})")
                break
        if fn not in ao13_images:
            print(f"\n[!] 13.3 texture for FileNum {fn} not found in {ao13_textures_dir}")

    # Load target images (TSAO) - we'll modify these
    tsao_images = {}
    tsao_paths = {}
    for fn in tsao_by_file:
        for ext in ['.png', '.bmp', '.BMP', '.PNG']:
            img_path = tsao_textures_dir / f"{fn}{ext}"
            if img_path.exists():
                tsao_images[fn] = Image.open(img_path).convert('RGBA')
                tsao_paths[fn] = img_path
                print(f"Loaded TSAO texture: {img_path} ({tsao_images[fn].size})")
                break
        if fn not in tsao_images:
            print(f"[!] TSAO texture for FileNum {fn} not found in {tsao_textures_dir}")

    if not ao13_images or not tsao_images:
        print("\nCannot proceed - missing textures.")
        return

    # Map: for each common frame GRH, copy from 13.3 source rect → TSAO dest rect
    common_frames = tsao_frames & ao13_frames
    copied = 0
    for fid in sorted(common_frames):
        if fid not in tsao_grh or fid not in ao13_grh:
            continue
        tsao_e = tsao_grh[fid]
        ao13_e = ao13_grh[fid]

        if tsao_e['numFrames'] != 1 or ao13_e['numFrames'] != 1:
            continue

        src_fn = ao13_e['fileNum']
        dst_fn = tsao_e['fileNum']

        if src_fn not in ao13_images or dst_fn not in tsao_images:
            print(f"  GRH {fid}: missing texture (src={src_fn}, dst={dst_fn})")
            continue

        src_img = ao13_images[src_fn]
        dst_img = tsao_images[dst_fn]

        # Source region from 13.3
        sx, sy = ao13_e['sX'], ao13_e['sY']
        sw, sh = ao13_e['pixelWidth'], ao13_e['pixelHeight']

        # Dest region in TSAO
        dx, dy = tsao_e['sX'], tsao_e['sY']
        dw, dh = tsao_e['pixelWidth'], tsao_e['pixelHeight']

        # Crop from source
        src_crop = src_img.crop((sx, sy, sx + sw, sy + sh))

        # If sizes differ, resize source to match dest
        if (sw, sh) != (dw, dh):
            print(f"  GRH {fid}: resizing {sw}x{sh} → {dw}x{dh}")
            src_crop = src_crop.resize((dw, dh), Image.LANCZOS)

        # Paste onto dest
        dst_img.paste(src_crop, (dx, dy))
        copied += 1
        print(f"  GRH {fid}: copied ({sx},{sy} {sw}x{sh}) from file {src_fn} → ({dx},{dy} {dw}x{dh}) in file {dst_fn}")

    print(f"\nCopied {copied} frame regions.")

    # Save modified TSAO textures
    for fn, img in tsao_images.items():
        out_path = tsao_paths[fn]
        # Backup
        backup_path = out_path.with_suffix(out_path.suffix + '.bak')
        if not backup_path.exists():
            import shutil
            shutil.copy2(out_path, backup_path)
            print(f"Backed up: {out_path} → {backup_path}")
        img.save(out_path)
        print(f"Saved: {out_path}")

    print("\nDone! To revert: restore from .bak files or git checkout.")


if __name__ == '__main__':
    main()
