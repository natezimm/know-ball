# Who's That real-photo browser review

October 19, 2026. Baseline main: `c19ed25ad57fffe0bfaba01813d7ddd8d1e5f2d4`.
Local test instance only; no production changes. The four prepared photographs were not scheduled.

## Finding and fix

The first real-photo run failed on LeBron's tall silhouette at 390px. The image's intrinsic
dimensions expanded the shared stage about 51px beyond its intended 4:3 height. The old
square synthetic inputs did not expose this. Tall images now sit inside a positioned 4:3
frame, fitted with `object-fit: contain`; the silhouette is bottom aligned, photos centered.
Visual inspection then found long license URLs clipped by the stage's overflow boundary at
390px, even though the page itself did not scroll sideways. Credit spans now wrap long words
and URLs within the available width, with separate checks for clipped attribution text.
The admin preview and public game use that same component.

The normal admin browser test now uploads 200×800 synthetic portraits and checks image
loading, frame shape and containment for the silhouette, blurred clue and reveal. It also
continues to cover editing, preview, attribution and future scheduling with synthetic data.
This regression runs in normal CI without requiring the licensed photos.

## Real-content result

| Prepared input | 390×844 Chromium | 1280×844 Chromium | Draft state after review |
| --- | --- | --- | --- |
| LeBron chalk toss, 2008 | Pass | Pass | Unscheduled |
| Dirk fadeaway, 2008 | Pass | Pass | Unscheduled |
| Dr. J cocked-back dunk, 1981 | Pass | Pass | Unscheduled |
| Kobe rim hang, 2005 | Pass | Pass | Unscheduled |

The opt-in [real-content browser tests](../web/know-ball/e2e/curated-photos.spec.ts) check:

- Each original and cutout SHA-256 matches the committed media-validation evidence.
- The existing Python loader creates drafts through the real HTTP API. A second run skips
  the same IDs and does not create duplicates.
- The stored caption, year, team, source, credit and license match the manifest exactly.
- Silhouette, year, team, third/fourth-miss blurred clue and reveal display correctly.
- All images load, stay inside a 4:3 stage, and cause no horizontal page overflow.
- Reveal displays each full credit/license and source link; resetting to silhouette hides them.
- Previewing leaves player stats unchanged and every imported puzzle unscheduled.

Eight real-photo cases pass. The production Angular build and all 101 Angular tests pass.
The three ordinary admin/Who's That browser tests pass, including the new portrait regression.
The local browser was Chromium 153.0.8010.0, launched through Playwright's existing executable
override because its bundled browser download was unavailable in this environment.

The opt-in run adds two name-only fixture rows (Dr. J and Dirk) to the disposable history
import, with no invented biographical values. The bundled seed and production data are unchanged.
Real licensed photographs remain outside git; the existing media packs retain their credits.

## Repeat the review

Extract both `KnowBall_Iconic_Pair_Media.zip` and `KnowBall_DrJ_Kobe_Media.zip` so that their
`iconic-pair/` and `next-pair/` folders share a parent such as `artifacts/whos-that/`.
Use .NET 10, Node 22+ and Python 3.10+. From `web/know-ball`, run:

```sh
npm ci
npx playwright install chromium
npm run build
E2E_CURATED_ASSETS="$PWD/../../artifacts/whos-that" npx playwright test e2e/curated-photos.spec.ts --workers=1 --output ../../artifacts/curated-photo-review
```

The flag forces a new local server rather than reusing an existing one. The test server uses
its disposable `artifacts/e2e` SQLite database and media directory. The run replaces that
test workspace, imports local fixture data and uses a test admin key. Never point this
workflow at production. Input-file errors or hash mismatches fail the opt-in run. Without
the flag, normal CI skips the eight cases and still runs the synthetic portrait regression.
Optional `E2E_CHROMIUM` selects an installed browser; it is not required normally.

Local screenshots are written to each test's output folder for visual inspection. They are
review aids, not physical-device evidence. No admin key or licensed media is committed.

## Remaining gates

This completes a local admin-preview checkpoint. It does not approve the collection for
production or close the launch checklist. The first-five pilot still needs a fifth prepared
photo. Mask/difficulty approval, a real-photo browser-form upload in the production workflow,
production draft IDs and scheduled dates, physical iOS/Android gameplay, and installed-PWA
checks remain pending. The released Wilt “100” crop remains the strongest next candidate
documented in [the next-pair report](whos-that-next-pair.md).
