# Who's That: selected open-license pair and renewed search

Update October 19, 2026: [Dr. J and Kobe are now prepared](whos-that-next-pair.md), with
six additional candidate checks including Wilt's released “100” crop. The exact-target
search below remains dated October 18; these alternatives depict different moments.

Checked 2026-10-18 America/New_York. The user selected LeBron's chalk toss first, then Dirk's
fadeaway, and requested another search for the other iconic photographs. This records findings;
an unsuccessful search does not prove that no open-license copy exists anywhere.

## Prepared pair

The [iconic-pair manifest](../content/whos-that/iconic-pair.json) records the exact sources,
dates, titles, original Flickr links, credits, adaptations and CC BY-SA 2.0 license URL.
User selection supplies iconic-content approval only. Admin preview and phone play remain open.

| Order | Source | Downloaded input | License evidence |
| --- | --- | --- | --- |
| 1 | [LeBron James' pregame ritual](https://commons.wikimedia.org/wiki/File:LeBron_James%27_pregame_ritual.jpg), Keith Allison, 2008-03-13 | 1,600 × 2,400; existing Commons crop by TonyTheTiger | File page lists CC BY-SA 2.0 and a Flickr review dated 2009-03-21; original Flickr photo 2334037655 |
| 2 | [Dirk Nowitzki / NowitzkiWizards1.jpg](https://commons.wikimedia.org/wiki/File:NowitzkiWizards1.jpg), Keith Allison, 2008-12-21 | 2,197 × 2,893; parent of NowitzkiFadeaway.jpg | File page lists CC BY-SA 2.0 and a Flickr review dated 2009-03-25; original Flickr photo 3126425237 |

Both inputs are unchanged downloaded files. Hand-traced alpha masks with narrow-band GrabCut
edge refinement preserve source RGB exactly; no anatomy or pose was generated. The body-only
LeBron silhouette excludes the cloud; the unchanged source and reveal retain the chalk toss.
Dirk uses the same photograph as the proposed fadeaway crop, with more of the bent leg visible.
His right shoe ends at the source frame and is not reconstructed.

Actual `SilhouetteRenderer` checks pass for both pairs: decoding, clear background, nonempty
subject and black-only visible silhouette pixels. The [validation report](../content/whos-that/iconic-pair-media-validation.json)
records dimensions and input SHA-256 hashes. Each asset folder also has mask.png and mask.json,
including the source-coordinate crop box and pixel-preservation result.

The separate **KnowBall_Iconic_Pair_Media.zip** contains originals, transparent cuts, masks,
rendered silhouette/blur/reveal copies, full credits, `review.html` and `review-sheet.jpg`.
All photo-derived image assets in that pack are distributed under CC BY-SA 2.0; retain credits,
source links, license links and transformation notices on reuse. See the
[license deed](https://creativecommons.org/licenses/by-sa/2.0/).

Extract its `iconic-pair/` folder under `artifacts/whos-that/`, then run from the repository root:

```sh
python3 scripts/curated-puzzles.py check --manifest content/whos-that/iconic-pair.json --assets artifacts/whos-that/iconic-pair
```

Both candidates pass offline preflight. With the existing admin key in the environment, create
unscheduled drafts in a test instance first:

```sh
python3 scripts/curated-puzzles.py upload --manifest content/whos-that/iconic-pair.json --assets artifacts/whos-that/iconic-pair --base-url http://127.0.0.1:5080
```

The configured production admin key was unavailable in this preparation environment; no drafts
were created or scheduled. Inspect all image stages and attribution in `/admin` before scheduling
LeBron, then Dirk. Record production IDs and physical-phone evidence before closing launch gates.
The old default starter-five.json remains rejected; use the explicit replacement manifest above.

## Renewed search: exact iconic targets

Searches covered both web indexes, Commons file pages and original/agency references, plus Flickr
leads. Queries paired each moment with `Creative Commons`, `CC BY`, `license`, `Flickr` or
`Wikimedia Commons`. No additional exact target was verified as openly licensed in this pass.

| Target | Outcome and evidence |
| --- | --- |
| LeBron's 2016 Game 7 block | Searches return Getty/NBAE listings, the NBA video and CC photos of other Finals plays. [Getty block listings](https://www.gettyimages.com/photos/lebron-james-iguodala) and [NBA video](https://www.nba.com/watch/video/channels/playoffs/finals/2016/06/22/lebron-james-big-block-on-iggy-game7.nba) do not establish an open photo license. Do not substitute the old June 16 dunk. |
| Jordan's 1988 free-throw-line dunk | [Walter Iooss/Sports Illustrated asset 627192818](https://www.gettyimages.com/detail/news-photo/slam-dunk-contest-chicago-bulls-michael-jordan-in-action-news-photo/627192818) remains a rights-managed listing requiring prior approval. CC labels on encyclopedia text did not establish a license for this photograph. |
| Kobe's 2010 scorer's-table celebration | [Andscape's photographer account and exact image](https://andscape.com/features/kobe-vs-kobe-bryant-lakers-numbers-8-and-24-jersey-retirement/) credits Garrett Ellwood/NBAE via Getty Images. No explicit open license was verified for that image. |
| Iverson's 2001 step-over | [Getty asset 638822](https://www.gettyimages.com/detail/news-photo/allen-iverson-of-the-philadelphia-76ers-steps-over-tyronn-news-photo/638822) requires prior approval. A search hit for a CC-republished Iverson article was not evidence that the step-over photo was licensed. |
| Wade–LeBron's famous December 6, 2010 dunk composition | The [CC BY-SA LeBron DWade.jpg](https://commons.wikimedia.org/wiki/File:LeBron_DWade.jpg) is Keith Allison's **December 18 Washington game**, not Morry Gash's Milwaukee photograph. Other search hits/reposts did not verify an open license for the famous frame. |
| Curry's Paris 2024 night-night gesture | [Clément Bardot's CC BY-SA 4.0 Olympic photograph](https://commons.wikimedia.org/wiki/File:Stephen_Curry,_Olympic_Games_2024.jpg) was taken **August 3**, before the August 10 gold-medal final. It is a different moment, so was not substituted. A [Flickr Night Night album](https://www.flickr.com/photos/brockzilla_collects/sets/72177720330349404) contains trading-card scans rather than a verified photographer-licensed original. The [Wally Skalij/Los Angeles Times Getty candidate](https://www.gettyimages.ie/detail/news-photo/august-10-2024-usas-stephen-curry-celebrates-the-win-news-photo/2165755114) remains rights-managed. |

The remaining targets stay unapproved until the exact photograph has verifiable reuse terms.
Article-wide CC notices, reposts and an uploader's license on a scan are insufficient evidence
that the underlying agency photograph was released. These findings do not rely on a fair-use
claim and do not authorize publishing the unverified photographs.
