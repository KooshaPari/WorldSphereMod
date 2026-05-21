#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import zipfile
from dataclasses import dataclass
from datetime import datetime
from io import BytesIO
from pathlib import Path
from typing import Dict, List, Optional, Tuple

try:
    from PIL import Image
except ModuleNotFoundError:  # pragma: no cover
    Image = None


SUPPORTED_BLOCK_PREFIX = "assets/minecraft/textures/block/"
PADDING = 2
DEFAULT_MAPPING = {
    "grass_block_top": "biome_grass",
    "grass_block_side": "biome_grass_side",
    "dirt": "biome_dirt",
    "stone": "mountain_rock",
    "cobblestone": "building_cobble",
    "oak_planks": "building_plank",
    "log_oak": "building_wood",
    "water_still": "water_surface",
    "water_flow": "water_surface",
    "sand": "desert_sand",
    "snow": "tundra_snow",
}
NORMAL_SUFFIXES = ("_n", "_normal", "_normal_map")


def parse_args() -> argparse.Namespace:
    default_output = (
        Path(os.environ.get("USERPROFILE", str(Path.home())))
        / "AppData"
        / "LocalLow"
        / "mkarpenko"
        / "WorldBox"
        / "mods_config"
        / "wsm3d-texturepack"
    )
    parser = argparse.ArgumentParser(description="Import Minecraft block textures into WSM3D atlas files.")
    parser.add_argument("pack_path", help="Path to Minecraft resource pack ZIP.")
    parser.add_argument("--output-dir", default=str(default_output), help="Directory for atlas + manifest output.")
    parser.add_argument("--max-atlas", type=int, default=4096, help="Maximum atlas size (power-of-two).")
    return parser.parse_args()


@dataclass
class AtlasRect:
    x: int
    y: int
    width: int
    height: int
    uv_x: float
    uv_y: float
    uv_width: float
    uv_height: float


def file_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fp:
        for chunk in iter(lambda: fp.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_pack_meta(zip_path: Path) -> None:
    with zipfile.ZipFile(zip_path, "r") as zf:
        if "pack.mcmeta" not in zf.namelist():
            raise RuntimeError("missing pack.mcmeta")
        with zf.open("pack.mcmeta") as fp:
            meta = json.loads(fp.read().decode("utf-8", errors="replace"))
        pack_info = meta.get("pack", {})
        pack_format = int(pack_info.get("pack_format", 0))
        if pack_format <= 0 or pack_format > 120:
            raise RuntimeError(f"unsupported pack_format={pack_format}")


def read_block_textures(zip_path: Path) -> Dict[str, bytes]:
    with zipfile.ZipFile(zip_path, "r") as zf:
        textures: Dict[str, bytes] = {}
        for name in zf.namelist():
            if not name.lower().endswith(".png"):
                continue
            norm = name.replace("\\", "/")
            if not norm.startswith(SUPPORTED_BLOCK_PREFIX):
                continue
            rel = norm[len(SUPPORTED_BLOCK_PREFIX):]
            if "/" in rel:
                continue
            textures[rel.lower()] = zf.read(name)
    return textures


def collect_known_textures(textures: Dict[str, bytes]) -> Tuple[List[Tuple[str, str, bytes]], Dict[str, Tuple[str, bytes]]]:
    rgb_items: List[Tuple[str, str, bytes]] = []
    normal_items: Dict[str, Tuple[str, bytes]] = {}

    for mc_name, wsm3d_class in DEFAULT_MAPPING.items():
        png = f"{mc_name}.png"
        if png in textures:
            rgb_items.append((mc_name, wsm3d_class, textures[png]))

        for suffix in NORMAL_SUFFIXES:
            normal_png = f"{mc_name}{suffix}.png"
            if normal_png in textures:
                normal_items[mc_name] = (normal_png, textures[normal_png])
                break

    return rgb_items, normal_items


def decode_images(items: List[Tuple[str, bytes]]) -> Dict[str, Tuple[AtlasRect, object]]:
    decoded = {}
    for name, data in items:
        image = Image.open(BytesIO(data)).convert("RGBA")
        decoded[name] = (None, image)
    return decoded


def choose_atlas_size(items: List[Tuple[str, bytes]], max_size: int) -> Tuple[int, Dict[str, AtlasRect], Dict[str, object]]:
    if not items:
        raise RuntimeError("no images to pack")

    decoded = {name: Image.open(BytesIO(data)).convert("RGBA") for name, data in items}
    sorted_names = sorted(decoded.keys(), key=lambda n: decoded[n].width * decoded[n].height, reverse=True)

    for size in [128, 256, 512, 1024, 2048, 4096, 8192]:
        if size > max_size:
            break
        placements: Dict[str, AtlasRect] = {}
        x = PADDING
        y = PADDING
        row_height = 0
        cursor_failed = False

        for name in sorted_names:
            im = decoded[name]
            cell_w = im.width + (PADDING * 2)
            cell_h = im.height + (PADDING * 2)

            if x + cell_w + PADDING > size:
                x = PADDING
                y += row_height + PADDING
                row_height = 0

            if y + cell_h + PADDING > size:
                cursor_failed = True
                break

            px = x + PADDING
            py = y + PADDING
            placements[name] = AtlasRect(
                x=px,
                y=py,
                width=im.width,
                height=im.height,
                uv_x=px / size,
                uv_y=1.0 - ((py + im.height) / size),
                uv_width=im.width / size,
                uv_height=im.height / size,
            )
            x += cell_w + PADDING
            row_height = max(row_height, cell_h)

        if not cursor_failed:
            return size, placements, decoded

    raise RuntimeError("textures do not fit in configured max_atlas")


def write_atlas(atlas_size: int, items: List[Tuple[str, bytes]], placements: Dict[str, AtlasRect], out_path: Path) -> None:
    atlas = Image.new("RGBA", (atlas_size, atlas_size), (0, 0, 0, 0))
    for name, data in items:
        placement = placements[name]
        with Image.open(BytesIO(data)) as im:
            atlas.paste(im.convert("RGBA"), (placement.x, placement.y), im.convert("RGBA"))
    out_path.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out_path, format="PNG")


def safe_pack_name(path: Path) -> str:
    return re.sub(r"[^a-zA-Z0-9._-]+", "-", path.stem.lower())


def main() -> int:
    args = parse_args()
    if Image is None:
        print("Missing dependency: Pillow. Install with `python -m pip install Pillow`.")
        return 1

    pack_path = Path(args.pack_path).expanduser()
    if not pack_path.exists():
        print(f"Pack not found: {pack_path}")
        return 1
    if args.max_atlas <= 0 or (args.max_atlas & (args.max_atlas - 1)) != 0:
        print("--max-atlas must be positive power-of-two")
        return 1

    try:
        read_pack_meta(pack_path)
    except Exception as err:
        print(f"Invalid pack: {err}")
        return 1

    textures = read_block_textures(pack_path)
    rgb_items, normal_items = collect_known_textures(textures)
    if not rgb_items:
        print("No mapped textures found in this pack.")
        return 0

    rgb_input = [(name + ".png", data) for name, _class_name, data in rgb_items]
    try:
        rgb_size, rgb_placements, rgb_decoded = choose_atlas_size(rgb_input, args.max_atlas)
    except Exception as err:
        print(f"Failed to pack RGB atlas: {err}")
        return 1

    normal_input = [(name, data) for name, (_, data) in normal_items.items()]
    normal_size = None
    normal_placements = None
    if normal_input:
        try:
            normal_size, normal_placements, _ = choose_atlas_size(normal_input, args.max_atlas)
        except Exception as err:
            print(f"Failed to pack normal atlas: {err}")

    manifest_hash = file_hash(pack_path)
    out_root = Path(args.output_dir).expanduser()
    output_dir = out_root / f"{safe_pack_name(pack_path)}_{manifest_hash[:10]}"
    output_dir.mkdir(parents=True, exist_ok=True)

    rgb_atlas = "atlas_rgb.png"
    rgb_atlas_path = output_dir / rgb_atlas
    write_atlas(rgb_size, rgb_input, rgb_placements, rgb_atlas_path)
    for image in rgb_decoded.values():
        try:
            image.close()
        except Exception:
            pass

    normal_atlas = None
    if normal_placements is not None:
        normal_atlas = "atlas_normal.png"
        write_atlas(normal_size or rgb_size, normal_input, normal_placements, output_dir / normal_atlas)

    mappings = []
    for mc_name, wsm3d_class, _data in rgb_items:
        mapping: Dict[str, object] = {
            "mc_block_name": mc_name,
            "wsm3d_class": wsm3d_class,
            "rect": rgb_placements[f"{mc_name}.png"].__dict__,
        }
        if normal_placements is not None:
            normal_png = normal_items.get(mc_name)
            if normal_png is not None:
                mapping["normal_rect"] = normal_placements[normal_png[0]].__dict__
            else:
                mapping["normal_rect"] = None
        else:
            mapping["normal_rect"] = None
        mappings.append(mapping)

    manifest = {
        "format": "wsm3d_mcpack_v1",
        "pack_name": pack_path.stem,
        "source_path": str(pack_path),
        "source_hash": manifest_hash,
        "created_utc": datetime.utcnow().isoformat(timespec="seconds") + "Z",
        "atlas_rgb": rgb_atlas,
        "atlas_normal": normal_atlas,
        "atlas_width": rgb_size,
        "atlas_height": rgb_size,
        "mappings": mappings,
    }

    manifest_path = output_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Imported {pack_path.name} -> {manifest_path}")
    print(f"RGBA atlas: {rgb_atlas_path}")
    if normal_atlas is not None:
        print(f"Normal atlas: {output_dir / normal_atlas}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
