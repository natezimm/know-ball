# Wilt's “100” — fifth prepared pilot

October 19, 2026. Selected explicitly by the user after the four-photo browser checkpoint.

Use [the released Commons crop](https://commons.wikimedia.org/wiki/File:Wilt_Chamberlain_100_Point_Game_1962.jpg),
2070×2472, current original file dated May 13, 2024, 19:10 UTC. Credit Paul Vathis / Associated
Press, existing crop/cleanup/blur by Hyperba21, and Know Ball's manual cutout/render adaptations.
The [manifest](../content/whos-that/wilt-100.json) contains the exact caption, March 2, 1962
date, Philadelphia Warriors team hint and displayed attribution.

## Source and reuse scope

Commons documents U.S. public-domain nonrenewal for the crop matching the released Wirephoto;
the wider AP frame is excluded. Copyright can persist outside the U.S. Retain that scope in
the status notice. This preparation does not rely on fair use or an open license from AP.

The [Library of Congress AP/Wide World note](https://wwws.loc.gov/rr/print/res/155_wide.html)
was retrieved during this pass. It describes a general search in which few registrations
were found and those were not renewed, while cautioning that individual images can remain
protected. It does not certify this particular photo. The linked Worthpoint release listing
returned 403, so release front/back and an individual renewal search were not independently
verified here. The crop-specific assessment remains the Commons record, not a blanket claim
about AP photographs.

## Prepared media

**KnowBall_Wilt_100_Media.zip** contains an unchanged source input, a manual transparent cutout,
full-size alpha mask, tracing/crop metadata, actual application silhouette/blur/reveal outputs,
credit/reuse notes, review.html and review-sheet.jpg. Media stays outside git.

The mask isolates Wilt's connected portrait, arms, hand and original paper. It excludes the
background and detached lower-right frame fragment. Cropped edges remain as photographed;
no anatomy or lettering was generated. Every retained RGB pixel matches its source location.
The black silhouette hides the paper's digits naturally. Do not add “100” to the silhouette;
assess recognition/difficulty using the 1962 and Philadelphia Warriors hints.

The [renderer report](../content/whos-that/wilt-100-media-validation.json) records decoding,
transparency, zero visible nonblack silhouette pixels, dimensions and SHA-256 hashes. File and
metadata preflight pass. The extended ten-case browser run passes for all five photos, including
Wilt at 390px and 1280px: real loader upload, exact input hashes, hint/reveal stages, image
containment, unclipped attribution, unchanged stats and unscheduled draft state.

Extract wilt-100/ under artifacts/whos-that/, then check from the repository root:

```sh
python3 scripts/curated-puzzles.py check --manifest content/whos-that/wilt-100.json --assets artifacts/whos-that/wilt-100
```

With the existing admin key configured, create an unscheduled draft in a test instance:

```sh
python3 scripts/curated-puzzles.py upload --manifest content/whos-that/wilt-100.json --assets artifacts/whos-that/wilt-100 --base-url http://127.0.0.1:5080
```

Follow the [browser-review instructions](whos-that-browser-review.md) after extracting all three
packs. The earlier four-photo result is historical; the extended run includes all five. The three
extra retired names in its disposable fixture have no invented biographical values.

Five exact photographs are now prepared: LeBron, Dirk, Dr. J, Kobe and Wilt. That completes media
preparation for the pilot, not final approval or publication. Mask/difficulty approval, physical
phone/PWA checks, production draft IDs and scheduling remain pending. No production change occurred.
