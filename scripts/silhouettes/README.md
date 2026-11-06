# Silhouette cut-outs

Research tooling for making a player's silhouette from a photo. Nothing here ships with the site, and no game
uses it yet. It exists to test whether better tools fix the cut-out quality that retired the photo game
(see [future games](../../docs/archive/future-games.md)).

The October 2026 pilot cut-outs were hand-drawn alpha masks refined with GrabCut (recorded in the removed
`content/whos-that/*-media-validation.json`), and they were rejected on visual quality. This pipeline replaces
that with:

1. **SAM 2.1** ([Apache 2.0](https://huggingface.co/facebook/sam2.1-hiera-large)) picks *which* object is the
   player, from clicks: points on the player and the ball, and "leave out" points on defenders or the rim.
2. **BiRefNet** ([MIT](https://huggingface.co/ZhengPeng7/BiRefNet)), run through
   [rembg](https://github.com/danielgatis/rembg) (MIT), decides *where* the edge is. It may only move SAM's
   outline by a narrow band, so it cannot add a defender back.
3. **Cleanup**: a solid mask, only the player's pieces kept, specks dropped, small holes filled (gaps between an
   arm and the body stay open), the outline smoothed, then traced to SVG with
   [potrace](https://potrace.sourceforge.net/) (GPL, run as a tool and not shipped).

Avoid BRIA RMBG-2.0: its weights are non-commercial without a paid agreement.

## Setup (macOS)

```sh
cd scripts/silhouettes
python3 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
brew install potrace
```

The first `run` downloads about 900 MB of SAM 2.1 weights from Hugging Face and about 950 MB of BiRefNet weights
from rembg's GitHub releases. On Apple silicon it uses the GPU (`mps`) when available.

## Use

Keep every photo and output under `artifacts/`, which git ignores. Do not commit photos, masks or cut-outs.

```sh
OUT=../../artifacts/silhouettes
python cutout.py fetch   pilot.json --out $OUT     # unchanged originals from Commons, plus CREDITS.md
python cutout.py grid    pilot.json --out $OUT     # <key>/grid.jpg: the photo with pixel coordinates
python cutout.py pick    pilot.json --out $OUT --key dirk-fadeaway-2008   # click points (needs a screen)
python cutout.py run     pilot.json --out $OUT --old ~/Downloads/whos-that
python cutout.py compare pilot.json --out $OUT --old ~/Downloads/whos-that
```

- `pick` saves clicks into `pilot.json`: left click on the player (and the ball), right click on what to leave
  out. You can also type points from `grid.jpg` by hand as `[x, y, 1]` or `[x, y, 0]`, or give a `box`.
- Something the player holds that SAM treats as a separate object (Wilt's "100" sign) goes in
  `"extra": [{"box": [x0, y0, x1, y1], "points": []}]` inside `prompts`. Each extra gets its own SAM pass and is
  merged into the player's mask; adding its clicks to the player's list instead makes SAM jump to it.
- With no points, `run` seeds prompts from the old cut-out (its box, points deep inside it, points far outside
  it). That is a fair first test of the tools; real use should click.
- Each `<key>/` gets `sam.png` (SAM's mask), `edge.png` (BiRefNet's), `mask.png`, `cutout.png` (source pixels
  with the new alpha), `silhouette.png`, `silhouette.svg`, `overlay.jpg` (the mask tinted on the photo, SAM's
  outline in yellow, the clicks) and `report.json`.
- `compare` draws photo, old silhouette and new silhouette in the old game's 4:3 phone frame (358 x 268 CSS px,
  drawn at 2x) into `<key>/compare.png` and one `compare-phone.png` sheet.
- `--old` points at the extracted review packs (`KnowBall_Iconic_Pair_Media.zip`, `KnowBall_DrJ_Kobe_Media.zip`,
  `KnowBall_Wilt_100_Media.zip`); any folder with `<key>/cutout.png` inside works.

`report.json` has two numbers to read beside the pictures: `edgeAgreesWithSam` (low means BiRefNet thought
something else was the subject, so look hard at the edge) and `roughness` (outline length against a circle of
the same area; a fringed or jagged edge scores higher than a clean one of the same shape).

Tuning, all per 1000 px of the photo's long side: `--band` (default 12) is how far BiRefNet may move SAM's edge,
`--smooth` (1.5) is outline smoothing. `--hole` and `--speck` are shares of the mask's area. `--no-edge` runs SAM
alone, and `--edge-model birefnet-general-lite` is faster.

## Rights

The five pilot photos are from Wikimedia Commons: two CC BY-SA 2.0, one CC BY 2.0 and two public domain in the
U.S. only. A cut-out or silhouette is an adaptation, so keep the credit in `CREDITS.md` with every copy, say
what was changed, and release adaptations of the CC BY-SA photos under CC BY-SA 2.0. Commons licences cover
copyright only, not the players' publicity rights or the team marks in the photo.
