# Who's That: Dr. J, then Kobe

Prepared October 19, 2026, after the LeBron/Dirk pilot in PR #15. The user selected
these exact photographs and their order. Selection approval does not approve the masks
or close the admin-preview and physical-phone checks.

The [next-pair manifest](../content/whos-that/next-pair.json) is separate from the earlier
pair and the rejected starter-five batch. Media stays outside git in the downloadable
**KnowBall_DrJ_Kobe_Media.zip**. Extract `next-pair/` under `artifacts/whos-that/`.

## Prepared photographs

| Order | Photograph | Credit and reuse terms | Evidence |
| --- | --- | --- | --- |
| 3 | [Dr. J's cocked-back dunk](https://commons.wikimedia.org/wiki/File:JuliusErvingSlamDunk1981.jpg), 1981, Atlanta | Jim Accordino, [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/); credit existing color correction by JoeJohnson2 and Know Ball adaptations | Commons records the license and a Flickr review dated August 14, 2009. Original Flickr photo 3805714889 returned 404. Source identifies only the year; no exact day is invented. |
| 4 | [Kobe hanging from the rim](https://commons.wikimedia.org/wiki/File:Kobe_Bryant_dunk.jpg), October 11, 2005, Hawaii | Cpl. Megan L. Stiner / U.S. Marine Corps; documented U.S. public-domain official federal work; retain source and adaptation credit | Same photograph, caption and photographer credit visually confirmed in [Hawaii Marine, October 14, 2005, C-1](https://d34w7g4gy10iej.cloudfront.net/pubs/pdf_24957.pdf#page=16). Legacy marines.mil source returned 403. |

The two photos have different reuse terms; do not label the whole pack CC BY-SA.
The Dr. J original and derivatives in this pack are supplied under CC BY 2.0.
No exclusive copyright is claimed in the faithful Kobe adaptations. Neither attribution
line implies endorsement. Each displayed credit includes an adaptation notice, and each
displayed license includes its source/license URL within the existing field limits.

The inputs are unchanged downloaded Commons files. Hand-traced alpha masks, refined in
a narrow band with GrabCut, retain source RGB exactly. No image content was generated.
Dr. J retains his ball and airborne pose. Kobe retains his crossed-leg pose and foreground
ball; the rim, net, other players and crowd are excluded from the cutout. The full source
remains in the reveal. The pack includes masks, coordinates, crop boxes and hashes.

The actual application `SilhouetteRenderer` decoded both pairs and produced nonempty,
transparent silhouettes with zero visible nonblack pixels. The
[validation report](../content/whos-that/next-pair-media-validation.json) records input
hashes and dimensions. Visual review of the local comparison sheet completed; final mask,
admin-preview, phone difficulty and installed-PWA approval remain pending.

The pack includes unchanged photos, transparent cuts, full-size masks, actual renderer
outputs, `CREDITS.md`, `review.html`, `review-sheet.jpg`, and import instructions. After
extracting, run from the repository root:

```sh
python3 scripts/curated-puzzles.py check --manifest content/whos-that/next-pair.json --assets artifacts/whos-that/next-pair
```

Both candidates pass offline preflight. With the existing admin key configured, create
unscheduled drafts in a test instance first:

```sh
python3 scripts/curated-puzzles.py upload --manifest content/whos-that/next-pair.json --assets artifacts/whos-that/next-pair --base-url http://127.0.0.1:5080
```

No production upload or scheduling occurred; the admin key is unavailable here. Inspect
attribution and all stages in `/admin`, then play on a physical phone before scheduling.

## Six new candidate checks

These findings supplement the user's local photo catalog. That Mac-local catalog was
not available in this workspace and was not present at its named path on GitHub main;
this file preserves the findings without replacing that local work. Checked October 19.

| Candidate | Evidence and disposition |
| --- | --- |
| [Wilt holding “100,” March 2, 1962](https://commons.wikimedia.org/wiki/File:Wilt_Chamberlain_100_Point_Game_1962.jpg) | Strongest newly identified exact historic moment. Credit Paul Vathis / Associated Press. Commons documents U.S. public-domain nonrenewal and explicitly limits this to the crop matching the released Wirephoto. Do not substitute the wider AP frame. Retain the existing cleanup/blur history by Hyperba21 when preparing. Commons warns that copyright can remain outside the U.S. Archive front/back links timed out and the linked Library of Congress page could not be retrieved in this check; that underlying evidence was not independently reverified. Candidate only, no cutout or import yet. |
| [Dr. J, 1981](https://commons.wikimedia.org/wiki/File:JuliusErvingSlamDunk1981.jpg) | Prepared above, CC BY 2.0, with existing color correction credited. This is not the 1976 ABA dunk contest frame. |
| [Kobe, 2005](https://commons.wikimedia.org/wiki/File:Kobe_Bryant_dunk.jpg) | Prepared above with corroborating contemporary USMC publication. This is not the 2010 scorer's-table celebration. |
| [Blake Griffin airborne dunk](https://commons.wikimedia.org/wiki/File:Blake_Griffin_dunking_1.jpg) | Keith Allison, [CC BY-SA 2.0](https://creativecommons.org/licenses/by-sa/2.0/), Clippers at Washington, December 14, 2013. Commons records a Flickr license check on February 28, 2014; source Flickr 11378270295. Keep credit, changes and share-alike terms on derivatives. Reserve candidate, not prepared. |
| [Russell and Auerbach, 1966 title](https://commons.wikimedia.org/wiki/File:Bill_Russell_and_Red_Auerbach_1966_Champions.jpg) | Credit Fred Keenan. Commons claims U.S. public domain through publication without notice. The linked auction page returned 502, and the related Commons original repeats the claim without resolving publication evidence. Keep pending; no mask or import. |
| [Jordan, 1985 dunk contest](https://commons.wikimedia.org/wiki/File:Michael_Jordan_dunk_contest_February_1985.jpg) | Uncredited Chicago Tribune scan, February 10, 1985 edition. Commons claims no notice plus no timely registration. Linked newspaper scan returned 403, so full-publication notice and registration evidence remain unverified. Keep pending; no mask or import. This is not the sought 1988 free-throw-line frame. |

The [earlier exact-target search](whos-that-photo-search-followup.md) remains useful for
LeBron's block, Jordan's 1988 dunk, Iverson's step-over, Wade/LeBron's December 6 photo,
Kobe's 2010 celebration and Curry's 2024 night-night. These new alternatives do not
establish open licenses for those different photographs. No fair-use claim is relied on.
