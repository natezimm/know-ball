# Who's That: first five photo candidates

Prepared 2026-10-18 after [PR #11](https://github.com/natezimm/know-ball/pull/11).
This is the next content-preparation step in the [launch plan](implementation-plan.md#8-whats-left).

The [starter manifest](../content/whos-that/starter-five.json) contains five sourced candidates, their
photo-specific facts, proposed captions, credits and license URLs. The source descriptions and metadata
were checked; image quality, transparent cut-outs, silhouettes and phone gameplay have **not** been approved.
Media is kept outside git. No production puzzles have been created or scheduled.
These are recognizable action-photo candidates; review whether their silhouettes make satisfying puzzles
before treating them as the final iconic-moment collection.

| Candidate | Photo date | Team hint | Source and recorded license | Review focus |
| --- | --- | --- | --- | --- |
| LeBron James | 2016-06-16 | Cleveland Cavaliers | [2016 Finals dunk, Erik Drost](https://commons.wikimedia.org/wiki/File:Lebron_dunking_finals_2016.jpg), CC BY 2.0 | A dunk, not the Game 7 block. Choose only LeBron for the cut-out. |
| Kobe Bryant | 2013-01-15 | Los Angeles Lakers | [Dunk against Milwaukee, Fido; crop/touch-up by Bagumba](https://commons.wikimedia.org/wiki/File:Kobe_Bryant_dunking_2013.jpg), CC BY 2.0 | Capture date comes from the description/original photo, not the derivative's March timestamp. Check the smaller crop's phone sharpness. |
| Stephen Curry | 2011-03-02 | Golden State Warriors | [Jump shot, Keith Allison](https://commons.wikimedia.org/wiki/File:Stephen_Curry_shooting.jpg), CC BY-SA 2.0 | Lowercase `shooting` identifies this file; uppercase `Shooting` is a different photograph. Keep the complete shooting pose. |
| Kevin Durant | 2011-03-14 | Oklahoma City Thunder | [Dunk against Washington, Keith Allison](https://commons.wikimedia.org/wiki/File:Kevin_Durant_dunk.jpg), CC BY-SA 2.0 | Use the photo's Thunder team. Retain hands/ball and remove the rim and other players. |
| Giannis Antetokounmpo | 2018-01-15 | Milwaukee Bucks | [Dunk in Washington, Keith Allison](https://commons.wikimedia.org/wiki/File:Giannis_Antetokounmpo_(39004611954).jpg), CC BY-SA 2.0 | Check whether the extended pose stays distinctive in black. The capture date precedes the upload date. |

The file pages link the original photographers and license evidence. Retain those source links and credits,
including Bagumba's existing edits to the Kobe crop. The manifest credits also disclose Know Ball's planned
cut-out, silhouette, blur and resize transformations. Its displayed license field includes the license URL;
the CC BY-SA candidates explicitly carry the same license for adapted images. Review those notices against
the files actually used, and preserve them if the assets are exported or reused elsewhere.

## Media preparation checkpoint — 2026-10-18

All five original photographs were downloaded unchanged. Four provisional AI-assisted cut-outs
(Kobe, Curry, Durant and Giannis) were run through the application's `SilhouetteRenderer` at
commit `64ce3bfdb5ef729268624e27ab43e6e8e087a7e0`. Decoding, transparency and rendering
succeeded; all visible silhouette pixels were black. See the
[machine validation report](../content/whos-that/starter-five-media-validation.json) for dimensions
and input SHA-256 hashes. These checks do **not** establish source-faithful outlines or approve gameplay.

The separate `KnowBall_Starter_Photo_Media.zip` review pack contains the unchanged originals,
four provisional cuts, rendered silhouette/blur/reveal outputs, attribution, and `review.html`.
Extract its `starter-five/` directory under `artifacts/whos-that/` to use the loader commands below.
The pack's README identifies the provisional images; do not publish them as approved source extractions.

Open issues:
- LeBron's cut-out is missing: the image editor rejected the extraction twice.
- The four generated cuts alter some source edges and proportions. Correct the masks against the originals.
  Durant's net-occluded hand and Giannis's backboard-occluded arms need particular care;
  preserve visible source anatomy rather than inventing hidden parts.
- Kobe's low-resolution source and Curry's cropped pose still need phone review.
- Full-batch preflight fails on LeBron's missing cut-out. The other four pass file preflight only.
- Admin preview, production draft IDs, physical-phone gameplay and installed-PWA checks remain pending.

Complete faithful masks and rerun rendering before creating drafts. No launch gate is closed by
this checkpoint.

## Prepare the assets

Open each source page and download **Original file**, rather than a search-result thumbnail. Save it under
`artifacts/whos-that/starter-five/<candidate-key>/photo.jpg`. Keep originals unchanged; the API resizes and
re-encodes its served copy.

Make a transparent PNG cut-out from that exact photograph, selecting just the answer player and the ball.
On a Mac, Preview's background-removal tool can be a starting point; inspect and refine the edges. Save it
beside the photo as `cutout.png`. The API makes the solid-black silhouette and blurred hint. A transparent
background alone is not evidence of a usable silhouette: inspect missing limbs, merged bodies, remaining
court lines, and detached ball/hand shapes in the rendered preview.

The filenames and keys are in the manifest. Store working images under ignored `artifacts/`; do not add
media or an admin key to git. A complete set has ten input files. Each must be nonempty and at most 20 MiB.
The server performs actual image decoding and transparency validation; the local check validates metadata,
paths, extensions and file sizes only.

## Check and create drafts

Python 3.10+ and the standard library are sufficient. From the repository root:

```sh
python3 scripts/curated-puzzles.py check --assets artifacts/whos-that/starter-five
```

This command is offline, needs no key and lists every missing pair. It exits 1 while preparation is incomplete.
Select one or more prepared candidates by repeating `--only <candidate-key>` on either command.

After the selected pairs are ready, set `KNOWBALL_ADMIN_KEY` in the terminal environment using your password
manager, then create drafts on a local/test instance:

```sh
python3 scripts/curated-puzzles.py upload --assets artifacts/whos-that/starter-five --base-url http://127.0.0.1:5080
```

Use the same command with `--base-url https://knowball.nathanzimmerman.com` when ready to create production
drafts. It requires the existing admin key; no new account or service is needed. The key is read from the
environment and sent only to admin endpoints. Redirects are refused, so enter the final HTTPS site URL.

Before the first POST, the loader checks all selected files, resolves every new player by exact name
(including retired players), and checks existing source URLs. It stops on ambiguous player data or a source
already attached to different player/year/team metadata. Matching existing puzzles are reported and left
unchanged, including scheduled/published ones. It never updates, deletes, or schedules a puzzle.

Uploads are sequential, not a transaction. Each success prints its key and puzzle ID immediately. A failed
POST is not automatically retried: inspect `/admin` first, then rerun. Serial reruns skip already-created
sources. Do not run two imports of the same batch concurrently; the API has no source-URL uniqueness key.
To correct an existing draft's images or caption, use the editor introduced in PR #11.

## Review five before scheduling thirty

For each draft, use `/admin` to inspect all three rendered images and every preview stage. Confirm the
answer, actual photo year/team, caption, photographer, adaptations and license notice. The preview itself
does not record player stats. For a full phone game before production publication, import the same pair
into a test instance and schedule it for that instance's today. Alternatively, after approving the production
preview, schedule the first pilot for today and test the live game; doing so locks that puzzle immediately.
Complete the phone game with reload/resume and reveal, and record which environment was tested.

Record evidence below before changing a launch checkbox. Schedule only approved puzzles; put the remaining
pilots on future consecutive dates so an erroneous draft does not become locked today. Dates are Eastern dates.
The first five are a pilot; the four-game launch still needs thirty reviewed, consecutively scheduled puzzles.

| Candidate | Draft ID | Cut-out/silhouette approved | Preview approved | Phone/browser/date | Scheduled date |
| --- | --- | --- | --- | --- | --- |
| lebron-finals-2016 | Pending | Pending | Pending | Pending | Pending |
| kobe-dunk-2013 | Pending | Pending | Pending | Pending | Pending |
| curry-jumper-2011 | Pending | Pending | Pending | Pending | Pending |
| durant-dunk-2011 | Pending | Pending | Pending | Pending | Pending |
| giannis-dunk-2018 | Pending | Pending | Pending | Pending | Pending |

The admin-form browser test from PR #11 uses synthetic PNGs. The batch-loader tests use mocked API responses
and local test bytes. Neither closes this real-content gate or the physical-device/PWA gates.
