"""One-off helper: cut the Baku pose sheets into individual transparent WebP files.

Not part of the shipped build. Usage:
    python tools/mascot-slice/slice.py <sheets_dir> <out_dir> [--only slug,slug,...] [--size 320]

The sheets are green-screen JPEGs with a text label under each pose. Each pose is
cut from a hand-measured cell (coordinates are in the 2000px-wide preview space of
a 2752x1536 sheet), the green background is keyed out with a soft alpha and
de-spilled, and the result is cropped to the visible pixels.
"""
import argparse
import os
import sys

import numpy as np
from scipy import ndimage
from PIL import Image

SHEET_W = 2752
PREVIEW_W = 2000
K = SHEET_W / PREVIEW_W

# (file, rows) where rows = (y0, y1, [(slug, center_x), ...]) in preview coordinates.
SHEETS = [
    ("Gemini_Generated_Image_1gp3kt1gp3kt1gp3.jpg", [
        (0, 352, [("greeting", 175), ("protective", 470), ("speed", 990), ("trustworthy", 1280), ("idea", 1560), ("celebrating", 1838)]),
        (405, 730, [("thinking", 175), ("acknowledge", 450), ("holding-dgb", 735), ("guard", 1010), ("wallet", 1280), ("surprised", 1560), ("approve", 1838)]),
        (785, 1074, [("proud", 175), ("welcome", 450), ("grateful", 735), ("determined", 1010), ("love", 1280), ("party", 1560), ("relaxed", 1838)]),
    ]),
    ("Gemini_Generated_Image_ba3lvgba3lvgba3l.jpg", [
        (0, 352, [("send-launch", 280), ("send-flying", 790), ("send-swipe", 1280), ("send-coin", 1760)]),
        (400, 726, [("send-deliver", 265), ("send-throw", 785), ("receive-catch", 1300), ("receive-welcome", 1770)]),
        (770, 1076, [("receive-secure", 270), ("receive-inbound", 780), ("receive-success", 1290), ("receive-gift", 1760)]),
    ]),
    ("Gemini_Generated_Image_otfpzxotfpzxotfp.jpg", [
        (0, 335, [("send-confirm", 270), ("receive-request", 660), ("error", 1015), ("waiting", 1420), ("success", 1790)]),
        (390, 710, [("locked", 270), ("support", 660), ("no-network", 1035), ("scan-qr", 1420), ("balance-rich", 1790)]),
        (780, 1072, [("no-funds", 195), ("notification", 540), ("chart-up", 810), ("security-tip", 1170), ("welcome-back", 1490), ("sleeping", 1800)]),
    ]),
]


def key_green(rgb: np.ndarray) -> np.ndarray:
    """Return RGBA with the green screen removed (soft alpha) and green spill suppressed."""
    f = rgb.astype(np.float32)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    dom = g - np.maximum(r, b)  # how much green dominates
    t0, t1 = 25.0, 80.0
    alpha = 1.0 - np.clip((dom - t0) / (t1 - t0), 0.0, 1.0)
    # de-spill: pull green down to the max of red/blue where it dominates
    limit = np.maximum(r, b) + 10.0
    g2 = np.where(g > limit, limit, g)
    out = np.stack([r, g2, b, alpha * 255.0], axis=-1)
    return np.clip(out, 0, 255).astype(np.uint8)


def drop_border_slivers(rgba: np.ndarray) -> np.ndarray:
    """Remove small pieces of a neighbouring pose that bleed in at the left/right cell edge."""
    mask = rgba[..., 3] > 40
    labels, n = ndimage.label(mask)
    if n <= 1:
        return rgba
    h, w = mask.shape
    areas = ndimage.sum(mask, labels, range(1, n + 1))
    biggest = max(areas)
    out = rgba.copy()
    for i in range(1, n + 1):
        ys, xs = np.where(labels == i)
        touches_side = xs.min() <= 1 or xs.max() >= w - 2
        if touches_side and areas[i - 1] < biggest * 0.2:
            out[labels == i, 3] = 0
    return out


def cell_bounds(centers):
    xs = [c for _, c in centers]
    edges = [0]
    for a, b in zip(xs, xs[1:]):
        edges.append((a + b) / 2)
    edges.append(PREVIEW_W)
    return edges


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("sheets_dir")
    ap.add_argument("out_dir")
    ap.add_argument("--only", default="")
    ap.add_argument("--size", type=int, default=288)
    args = ap.parse_args()
    only = {s for s in args.only.split(",") if s}
    os.makedirs(args.out_dir, exist_ok=True)
    count = 0
    for fname, rows in SHEETS:
        im = Image.open(os.path.join(args.sheets_dir, fname)).convert("RGB")
        for y0, y1, poses in rows:
            edges = cell_bounds(poses)
            for i, (slug, _) in enumerate(poses):
                if only and slug not in only:
                    continue
                box = (int(edges[i] * K), int(y0 * K), int(edges[i + 1] * K), int(y1 * K))
                rgba = drop_border_slivers(key_green(np.array(im.crop(box))))
                alpha = rgba[..., 3]
                ys, xs = np.where(alpha > 40)
                if len(xs) == 0:
                    print("empty", slug, file=sys.stderr)
                    continue
                pad = 6
                x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad, rgba.shape[1] - 1)
                yy0, yy1 = max(ys.min() - pad, 0), min(ys.max() + pad, rgba.shape[0] - 1)
                out = Image.fromarray(rgba[yy0:yy1 + 1, x0:x1 + 1], "RGBA")
                out.thumbnail((args.size, args.size), Image.LANCZOS)
                path = os.path.join(args.out_dir, slug + ".webp")
                out.save(path, "WEBP", quality=78, method=4)
                count += 1
    print("wrote", count, "poses to", args.out_dir)


if __name__ == "__main__":
    main()
