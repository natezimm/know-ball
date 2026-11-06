#!/usr/bin/env python3
"""Make player cut-outs and silhouettes from photos: SAM 2 picks the player, BiRefNet sharpens the edge.

Research tooling only. Nothing here ships with the site, and photos, masks and cut-outs stay out of git
(write them under artifacts/, which is ignored). See README.md in this folder.

    python cutout.py fetch   pilot.json --out ../../artifacts/silhouettes
    python cutout.py grid    pilot.json --out ../../artifacts/silhouettes
    python cutout.py run     pilot.json --out ../../artifacts/silhouettes [--old DIR] [--only KEY]
    python cutout.py compare pilot.json --out ../../artifacts/silhouettes [--old DIR]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

USER_AGENT = "KnowBallCutouts/1.0 (+https://github.com/natezimm/know-ball)"
SAM_MODEL = "facebook/sam2.1-hiera-large"
EDGE_MODEL = "birefnet-general"

# Phone stage from the old game: a 390 px wide screen, 16 px gutters, a 4:3 frame, drawn at 2x.
STAGE_W, STAGE_H, SCALE = 358, 268, 2


# ---------------------------------------------------------------- batch and files


def load_batch(path: Path, only: list[str] | None) -> list[dict]:
    batch = json.loads(path.read_text())
    photos = batch["photos"]
    if only:
        unknown = set(only) - {p["key"] for p in photos}
        if unknown:
            sys.exit(f"Unknown key(s): {', '.join(sorted(unknown))}")
        photos = [p for p in photos if p["key"] in only]
    return photos


def photo_path(out: Path, entry: dict) -> Path:
    suffix = Path(entry["commonsFile"]).suffix.lower() or ".jpg"
    return out / entry["key"] / f"photo{suffix}"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def find_old_cutout(old: Path | None, key: str) -> Path | None:
    """The old review packs keep each cut-out at <pack>/<key>/cutout.png; accept any depth."""
    if not old:
        return None
    direct = old / key / "cutout.png"
    if direct.exists():
        return direct
    hits = sorted(old.rglob(f"{key}/cutout.png"))
    return hits[0] if hits else None


def load_rgb(path: Path) -> Image.Image:
    return ImageOps.exif_transpose(Image.open(path)).convert("RGB")


def alpha_of(path: Path, size: tuple[int, int]) -> np.ndarray:
    image = Image.open(path)
    alpha = image.getchannel("A") if "A" in image.getbands() else image.convert("L")
    if alpha.size != size:
        alpha = alpha.resize(size, Image.LANCZOS)
    return np.asarray(alpha, dtype=np.uint8)


# ---------------------------------------------------------------- fetch


def cmd_fetch(args) -> None:
    out: Path = args.out
    credits = ["# Photo credits", "", "Keep this file with any copy of these photos or anything made from them.", ""]
    for entry in load_batch(args.batch, args.only):
        target = photo_path(out, entry)
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or args.force:
            name = urllib.parse.quote(entry["commonsFile"].replace(" ", "_"))
            url = f"https://commons.wikimedia.org/wiki/Special:FilePath/{name}"
            print(f"{entry['key']}: downloading {entry['commonsFile']}")
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(request, timeout=120) as response, target.open("wb") as file:
                shutil.copyfileobj(response, file)
            time.sleep(1)  # be polite to Commons
        digest = sha256(target)
        expected = entry.get("sha256")
        if expected and digest != expected:
            message = f"{entry['key']}: SHA-256 differs from the pilot's file ({digest[:12]} vs {expected[:12]})."
            if args.strict:
                sys.exit(message)
            print("WARNING " + message + " Commons may have a newer upload; check its file history.")
        else:
            print(f"{entry['key']}: ok {target}")
        credits += [
            f"## {entry['player']} ({entry['key']})",
            "",
            f"- Source: {entry['sourceUrl']}",
            f"- Credit: {entry['credit']}",
            f"- Licence: {entry['license']} ({entry['licenseUrl']})",
            f"- File SHA-256: {digest}",
            "",
        ]
    (out / "CREDITS.md").write_text("\n".join(credits))


# ---------------------------------------------------------------- grid (for choosing points)


def cmd_grid(args) -> None:
    """Write each photo with a labelled pixel grid, so click points can be read off by eye."""
    for entry in load_batch(args.batch, args.only):
        photo = load_rgb(photo_path(args.out, entry))
        w, h = photo.size
        step = args.step or max(50, round(max(w, h) / 20 / 50) * 50)
        image = photo.copy()
        draw = ImageDraw.Draw(image)
        font = _font(max(14, w // 80))
        for x in range(0, w, step):
            draw.line([(x, 0), (x, h)], fill=(255, 0, 255), width=1)
            draw.text((x + 3, 3), str(x), fill=(255, 0, 255), font=font)
        for y in range(0, h, step):
            draw.line([(0, y), (w, y)], fill=(0, 255, 255), width=1)
            draw.text((3, y + 3), str(y), fill=(0, 255, 255), font=font)
        _draw_prompts(draw, entry["prompts"], w)
        target = args.out / entry["key"] / "grid.jpg"
        image.save(target, quality=88)
        print(f"{entry['key']}: {target} ({w}x{h}, grid every {step} px)")


# ---------------------------------------------------------------- pick (interactive, for a person)


def cmd_pick(args) -> None:
    """Click on the photo: left = player, right = leave out, middle/u = undo, q = save and close."""
    import matplotlib.pyplot as plt

    batch = json.loads(args.batch.read_text())
    entry = next((p for p in batch["photos"] if p["key"] == args.key), None)
    if entry is None:
        sys.exit(f"Unknown key: {args.key}")
    photo = load_rgb(photo_path(args.out, entry))
    points = [list(p) for p in entry["prompts"]["points"]]
    fig, ax = plt.subplots(figsize=(8, 10))
    ax.imshow(photo)
    ax.set_title(f"{entry['player']}: left click = player, right click = leave out, u = undo, q = save")
    marks = []

    def redraw():
        for m in marks:
            m.remove()
        marks.clear()
        for x, y, label in points:
            marks.append(ax.scatter([x], [y], c="lime" if label else "red", marker="o" if label else "x", s=80))
        fig.canvas.draw_idle()

    def on_click(event):
        if event.inaxes != ax or event.xdata is None:
            return
        if event.button == 2:
            if points:
                points.pop()
        else:
            points.append([round(event.xdata), round(event.ydata), 1 if event.button == 1 else 0])
        redraw()

    def on_key(event):
        if event.key == "u" and points:
            points.pop()
            redraw()

    fig.canvas.mpl_connect("button_press_event", on_click)
    fig.canvas.mpl_connect("key_press_event", on_key)
    redraw()
    plt.show()
    entry["prompts"]["points"] = points
    args.batch.write_text(json.dumps(batch, indent=2, ensure_ascii=False) + "\n")
    print(f"Saved {len(points)} point(s) for {entry['key']} to {args.batch}")


# ---------------------------------------------------------------- models


def torch_device(choice: str):
    import torch

    if choice != "auto":
        return torch.device(choice)
    if torch.backends.mps.is_available():
        return torch.device("mps")
    if torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


class Sam2:
    """SAM 2.1 through Hugging Face transformers (Apache 2.0 code and weights)."""

    def __init__(self, model: str, device: str):
        import torch
        from transformers import Sam2Model, Sam2Processor

        self.torch = torch
        self.device = torch_device(device)
        self.processor = Sam2Processor.from_pretrained(model)
        self.model = Sam2Model.from_pretrained(model).to(self.device).eval()

    def mask(self, photo: Image.Image, prompts: dict) -> tuple[np.ndarray, float]:
        points = prompts.get("points") or []
        box = prompts.get("box")
        kwargs = {}
        if points:
            kwargs["input_points"] = [[[[p[0], p[1]] for p in points]]]
            kwargs["input_labels"] = [[[int(p[2]) for p in points]]]
        if box:
            kwargs["input_boxes"] = [[list(map(float, box))]]
        if not kwargs:
            raise ValueError("no points or box")
        inputs = self.processor(images=photo, return_tensors="pt", **kwargs).to(self.device)
        multimask = len(points) <= 1 and not box  # one click is ambiguous; let SAM offer three
        with self.torch.no_grad():
            outputs = self.model(**inputs, multimask_output=multimask)
        masks = self.processor.post_process_masks(outputs.pred_masks.cpu(), inputs["original_sizes"])[0]
        scores = outputs.iou_scores.cpu()[0, 0]
        best = int(scores.argmax()) if multimask else 0
        return masks[0, best].numpy().astype(bool), float(scores[best])


class EdgeModel:
    """BiRefNet through rembg (MIT code, MIT weights). rembg downloads the weights on first use."""

    def __init__(self, model: str):
        from rembg import new_session

        self.session = new_session(model)

    def alpha(self, photo: Image.Image) -> np.ndarray:
        from rembg import remove

        mask = remove(photo, session=self.session, only_mask=True)
        return np.asarray(mask.convert("L"), dtype=np.float32) / 255.0


# ---------------------------------------------------------------- mask maths


def disk(radius: int) -> np.ndarray:
    radius = max(1, int(radius))
    return cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * radius + 1, 2 * radius + 1))


def seed_prompts_from(mask: np.ndarray, positives: int = 4, negatives: int = 2) -> dict:
    """Prompts taken from an earlier mask: its box, points deep inside it, and points far outside it in the box."""
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        raise ValueError("old mask is empty")
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())
    points = []
    inside = cv2.distanceTransform(mask.astype(np.uint8), cv2.DIST_L2, 5)
    for _ in range(positives):
        y, x = np.unravel_index(int(inside.argmax()), inside.shape)
        if inside[y, x] <= 2:
            break
        points.append([int(x), int(y), 1])
        cv2.circle(inside, (int(x), int(y)), int(inside[y, x] * 2.5), 0, -1)
    outside = cv2.distanceTransform((~mask).astype(np.uint8), cv2.DIST_L2, 5)
    in_box = np.zeros_like(outside)
    in_box[y0:y1 + 1, x0:x1 + 1] = outside[y0:y1 + 1, x0:x1 + 1]
    for _ in range(negatives):
        y, x = np.unravel_index(int(in_box.argmax()), in_box.shape)
        if in_box[y, x] < 0.05 * max(x1 - x0, y1 - y0):
            break  # nothing far enough outside the player to be worth excluding
        points.append([int(x), int(y), 0])
        cv2.circle(in_box, (int(x), int(y)), int(in_box[y, x] * 1.5), 0, -1)
    return {"box": [x0, y0, x1, y1], "points": points}


def combine(sam: np.ndarray, edge: np.ndarray | None, band: int) -> np.ndarray:
    """SAM decides which object; BiRefNet decides where its edge is, but only near SAM's outline."""
    if edge is None:
        return sam.astype(np.float32)
    sam8 = sam.astype(np.uint8)
    core = cv2.erode(sam8, disk(band)).astype(bool)
    near = cv2.dilate(sam8, disk(band)).astype(bool)
    alpha = np.where(near, edge, 0.0)
    alpha[core] = 1.0
    return alpha


def clean(alpha: np.ndarray, sam: np.ndarray, keep_points: list, threshold: float,
          speck: float, hole: float, smooth: int) -> tuple[np.ndarray, dict]:
    """Solid mask, the player's pieces only, small holes filled, specks dropped, edge smoothed."""
    solid = (alpha >= threshold).astype(np.uint8)
    area = max(int(solid.sum()), 1)

    # Keep the pieces that SAM chose (or that hold a player click); drop specks anywhere.
    count, labels, stats, _ = cv2.connectedComponentsWithStats(solid, connectivity=8)
    sizes = stats[:, cv2.CC_STAT_AREA]
    on_sam = np.bincount(labels[sam], minlength=count)
    keep = (sizes >= speck * area) & (on_sam >= 0.5 * sizes)
    h, w = solid.shape
    for x, y, label in keep_points:
        if label and 0 <= y < h and 0 <= x < w:
            keep[labels[y, x]] = True  # a piece the operator clicked on always stays
    keep[0] = False
    solid = keep[labels].astype(np.uint8)
    dropped = int(count - 1 - keep.sum())

    # Fill holes smaller than `hole` of the mask's area; bigger gaps (between an arm and the body) are real.
    inverse = (1 - solid).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(inverse, connectivity=4)
    x, y, bw, bh, a = (stats[:, i] for i in range(5))
    inside = (x > 0) & (y > 0) & (x + bw < w) & (y + bh < h)
    fill = inside & (a < hole * area)
    fill[0] = False
    solid[fill[labels]] = 1
    filled = int(fill.sum())

    # Smooth the outline: close then open, then blur and re-threshold to take off the stair steps.
    if smooth > 0:
        solid = cv2.morphologyEx(solid, cv2.MORPH_CLOSE, disk(smooth))
        solid = cv2.morphologyEx(solid, cv2.MORPH_OPEN, disk(max(1, smooth // 2)))
        blurred = cv2.GaussianBlur(solid.astype(np.float32), (0, 0), smooth)
        solid = (blurred >= 0.5).astype(np.uint8)

    pieces = cv2.connectedComponents(solid, connectivity=8)[0] - 1
    return solid.astype(bool), {"piecesDropped": dropped, "holesFilled": filled, "pieces": int(pieces)}


def iou(a: np.ndarray, b: np.ndarray) -> float:
    union = (a | b).sum()
    return float((a & b).sum() / union) if union else 0.0


def roughness(mask: np.ndarray) -> float:
    """Outline length over the length of a circle of the same area; jagged or fringed edges score higher."""
    contours, _ = cv2.findContours(mask.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    perimeter = sum(cv2.arcLength(c, True) for c in contours)
    area = mask.sum()
    return float(perimeter / (2 * np.sqrt(np.pi * area))) if area else 0.0


# ---------------------------------------------------------------- outputs


def save_outputs(folder: Path, photo: Image.Image, mask: np.ndarray, potrace_args: list[str]) -> bool:
    alpha = Image.fromarray((mask * 255).astype(np.uint8))
    alpha.save(folder / "mask.png")
    cutout = photo.convert("RGBA")
    cutout.putalpha(alpha)
    cutout.save(folder / "cutout.png")
    black = Image.new("RGBA", photo.size, (0, 0, 0, 0))
    black.putalpha(alpha)
    black.save(folder / "silhouette.png")
    potrace = shutil.which("potrace")
    if not potrace:
        return False
    pbm = folder / "mask.pbm"
    Image.fromarray(((~mask) * 255).astype(np.uint8)).convert("1").save(pbm)  # potrace traces black
    subprocess.run([potrace, str(pbm), "-s", "-o", str(folder / "silhouette.svg"), *potrace_args], check=True)
    pbm.unlink()
    return True


def save_overlay(folder: Path, photo: Image.Image, mask: np.ndarray, sam: np.ndarray, prompts: dict) -> None:
    """The photo with the final mask tinted, SAM's outline in yellow, and the click points."""
    base = np.asarray(photo, dtype=np.float32)
    tint = base.copy()
    tint[mask] = tint[mask] * 0.45 + np.array([0, 120, 255]) * 0.55
    image = Image.fromarray(tint.astype(np.uint8))
    outline = cv2.dilate(sam.astype(np.uint8), disk(2)) - sam.astype(np.uint8)
    arr = np.asarray(image).copy()
    arr[outline.astype(bool)] = (255, 220, 0)
    image = Image.fromarray(arr)
    draw = ImageDraw.Draw(image)
    _draw_prompts(draw, prompts, photo.size[0])
    image.thumbnail((1400, 1400))
    image.save(folder / "overlay.jpg", quality=88)


def _draw_prompts(draw: ImageDraw.ImageDraw, prompts: dict, width: int) -> None:
    r = max(6, width // 120)
    if prompts.get("box"):
        draw.rectangle(prompts["box"], outline=(255, 220, 0), width=max(2, r // 3))
    for extra in prompts.get("extra") or []:
        _draw_prompts(draw, extra, width)
    for x, y, label in prompts.get("points", []):
        if label:
            draw.ellipse([x - r, y - r, x + r, y + r], fill=(0, 220, 0), outline=(0, 0, 0), width=2)
        else:
            draw.line([x - r, y - r, x + r, y + r], fill=(255, 0, 0), width=max(3, r // 2))
            draw.line([x - r, y + r, x + r, y - r], fill=(255, 0, 0), width=max(3, r // 2))


def _font(size: int):
    for name in ("DejaVuSans.ttf", "Arial.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
                 "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


# ---------------------------------------------------------------- run


def cmd_run(args) -> None:
    sam_model = edge_model = None
    summary = []
    for entry in load_batch(args.batch, args.only):
        key = entry["key"]
        folder = args.out / key
        photo_file = photo_path(args.out, entry)
        if not photo_file.exists():
            sys.exit(f"{key}: no photo at {photo_file}; run fetch first")
        photo = load_rgb(photo_file)
        old_file = find_old_cutout(args.old, key)
        old = alpha_of(old_file, photo.size) >= 128 if old_file else None

        prompts = entry["prompts"]
        source = "batch"
        if not (prompts.get("points") or prompts.get("box")):
            if old is None:
                print(f"{key}: skipped, no prompts in the batch and no old cut-out to seed them from")
                continue
            prompts = seed_prompts_from(old)
            source = "seeded from old cut-out"

        if args.sam_mask:
            sam = alpha_of(args.sam_mask / key / "sam.png", photo.size) >= 128
            score = float("nan")
        else:
            sam_model = sam_model or Sam2(args.sam_model, args.device)
            sam, score = sam_model.mask(photo, prompts)
            # Extra objects (a sign, a ball held away from the body) get their own SAM pass and are merged in,
            # because piling their clicks onto the player's makes SAM jump to the bigger or plainer object.
            for extra in prompts.get("extra") or []:
                part, part_score = sam_model.mask(photo, extra)
                sam |= part
                score = min(score, part_score)
        Image.fromarray((sam * 255).astype(np.uint8)).save(folder / "sam.png")

        edge = None
        if not args.no_edge:
            edge_model = edge_model or EdgeModel(args.edge_model)
            edge = edge_model.alpha(photo)
            Image.fromarray((edge * 255).astype(np.uint8)).save(folder / "edge.png")

        scale = max(photo.size) / 1000  # tune radii for a 1000 px photo and scale them
        band = max(2, round(args.band * scale))
        smooth = round(args.smooth * scale)
        alpha = combine(sam, edge, band)
        clicks = prompts.get("points", []) + [p for e in prompts.get("extra") or [] for p in e.get("points", [])]
        mask, cleanup = clean(alpha, sam, clicks, args.threshold, args.speck, args.hole, smooth)
        traced = save_outputs(folder, photo, mask, ["--turdsize", str(round(20 * scale * scale)),
                                                    "--alphamax", "1.0", "--opttolerance", "0.2"])
        save_overlay(folder, photo, mask, sam, prompts)

        report = {
            "key": key,
            "photoSha256": sha256(photo_file),
            "size": list(photo.size),
            "prompts": prompts,
            "promptSource": source,
            "samModel": None if args.sam_mask else args.sam_model,
            "samScore": None if score != score else round(score, 3),
            "edgeModel": None if args.no_edge else args.edge_model,
            "edgeAgreesWithSam": None if edge is None else round(iou(sam, edge >= 0.5), 3),
            "coverage": round(float(mask.mean()), 4),
            "roughness": round(roughness(mask), 2),
            **cleanup,
            "svg": traced,
        }
        if old is not None:
            report["oldCutout"] = str(old_file)
            report["iouWithOld"] = round(iou(mask, old), 3)
            report["oldRoughness"] = round(roughness(old), 2)
        (folder / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        summary.append(report)
        note = f", IoU with old {report['iouWithOld']}" if old is not None else ""
        print(f"{key}: {report['pieces']} piece(s), roughness {report['roughness']}{note} -> {folder}")
    if not traced_any(summary):
        print("potrace was not found, so no SVG was written (brew install potrace).")


def traced_any(summary: list[dict]) -> bool:
    return not summary or any(r["svg"] for r in summary)


# ---------------------------------------------------------------- compare


def stage(image: Image.Image | None, label: str, background: tuple) -> Image.Image:
    """One phone-size 4:3 frame: the image fitted inside and bottom aligned, as the old game drew it."""
    w, h = STAGE_W * SCALE, STAGE_H * SCALE
    frame = Image.new("RGB", (w, h + 22 * SCALE), (255, 255, 255))
    inner = Image.new("RGB", (w, h), background)
    if image is not None:
        fitted = image.copy()
        fitted.thumbnail((w - 16 * SCALE, h - 12 * SCALE), Image.LANCZOS)
        x = (w - fitted.width) // 2
        y = h - fitted.height - 6 * SCALE
        inner.paste(fitted, (x, y), fitted if fitted.mode == "RGBA" else None)
    else:
        ImageDraw.Draw(inner).text((20, h // 2), "no old cut-out found", fill=(120, 120, 120), font=_font(14 * SCALE))
    frame.paste(inner, (0, 0))
    ImageDraw.Draw(frame).text((4 * SCALE, h + 4 * SCALE), label, fill=(40, 40, 40), font=_font(12 * SCALE))
    return frame


def black_silhouette(mask: np.ndarray) -> Image.Image:
    ys, xs = np.nonzero(mask)
    image = Image.new("RGBA", (mask.shape[1], mask.shape[0]), (0, 0, 0, 0))
    image.putalpha(Image.fromarray((mask * 255).astype(np.uint8)))
    return image.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)) if len(xs) else image


def cmd_compare(args) -> None:
    rows = []
    paper = (236, 228, 210)  # the site's card-stock tan; the exact value doesn't matter for judging shape
    for entry in load_batch(args.batch, args.only):
        key = entry["key"]
        folder = args.out / key
        if not (folder / "mask.png").exists():
            print(f"{key}: skipped, run it first")
            continue
        photo = load_rgb(photo_path(args.out, entry))
        new = alpha_of(folder / "mask.png", photo.size) >= 128
        old_file = find_old_cutout(args.old, key)
        old = alpha_of(old_file, photo.size) >= 128 if old_file else None
        cells = [
            stage(photo, f"{entry['player']}: photo", paper),
            stage(black_silhouette(old) if old is not None else None, "Before (old cut-out)", paper),
            stage(black_silhouette(new), "After (SAM 2 + BiRefNet)", paper),
        ]
        row = Image.new("RGB", (sum(c.width for c in cells) + 8 * SCALE * 2, cells[0].height), (255, 255, 255))
        x = 0
        for cell in cells:
            row.paste(cell, (x, 0))
            x += cell.width + 8 * SCALE
        row.save(folder / "compare.png")
        rows.append(row)
        print(f"{key}: {folder / 'compare.png'}")
    if rows:
        sheet = Image.new("RGB", (rows[0].width, sum(r.height + 12 * SCALE for r in rows)), (255, 255, 255))
        y = 0
        for row in rows:
            sheet.paste(row, (0, y))
            y += row.height + 12 * SCALE
        sheet.save(args.out / "compare-phone.png")
        print(f"Sheet: {args.out / 'compare-phone.png'}")


# ---------------------------------------------------------------- main


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    def common(p):
        p.add_argument("batch", type=Path, help="batch JSON, such as pilot.json")
        p.add_argument("--out", type=Path, required=True, help="working folder (keep it under artifacts/)")
        p.add_argument("--only", action="append", help="limit to one key; repeat for more")

    p = sub.add_parser("fetch", help="download the unchanged originals from Wikimedia Commons")
    common(p)
    p.add_argument("--force", action="store_true", help="download again even if the file exists")
    p.add_argument("--strict", action="store_true", help="stop when a file's hash differs from the batch")
    p.set_defaults(func=cmd_fetch)

    p = sub.add_parser("grid", help="draw a pixel grid on each photo to help choose click points")
    common(p)
    p.add_argument("--step", type=int, help="grid spacing in pixels")
    p.set_defaults(func=cmd_grid)

    p = sub.add_parser("pick", help="click points on one photo and save them to the batch (needs a screen)")
    p.add_argument("batch", type=Path)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--key", required=True)
    p.set_defaults(func=cmd_pick)

    p = sub.add_parser("run", help="make the mask, cut-out, silhouette PNG and SVG for each photo")
    common(p)
    p.add_argument("--old", type=Path, help="folder holding the old cut-outs (<key>/cutout.png), for seeding and IoU")
    p.add_argument("--sam-model", default=SAM_MODEL)
    p.add_argument("--edge-model", default=EDGE_MODEL, help="rembg model; birefnet-general-lite is faster")
    p.add_argument("--no-edge", action="store_true", help="skip BiRefNet and use SAM 2's mask alone")
    p.add_argument("--sam-mask", type=Path, help="use <dir>/<key>/sam.png instead of running SAM 2 (testing)")
    p.add_argument("--device", default="auto", help="auto, mps, cuda or cpu")
    p.add_argument("--band", type=float, default=12, help="how far (px per 1000) BiRefNet may move SAM's edge")
    p.add_argument("--threshold", type=float, default=0.5, help="alpha at or above this is solid")
    p.add_argument("--speck", type=float, default=0.002, help="drop pieces smaller than this share of the mask")
    p.add_argument("--hole", type=float, default=0.004, help="fill holes smaller than this share of the mask")
    p.add_argument("--smooth", type=float, default=1.5, help="outline smoothing radius (px per 1000); 0 = off")
    p.set_defaults(func=cmd_run)

    p = sub.add_parser("compare", help="phone-size before/after sheet")
    common(p)
    p.add_argument("--old", type=Path, help="folder holding the old cut-outs (<key>/cutout.png)")
    p.set_defaults(func=cmd_compare)

    args = parser.parse_args(argv)
    args.func(args)


if __name__ == "__main__":
    main()
