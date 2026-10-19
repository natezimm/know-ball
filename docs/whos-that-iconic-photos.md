# Who's That: iconic photograph selection

Updated 2026-10-18 from the user's explicit requirement: **only iconic images of players—their
most famous photographs or signature moments.** A recognizable player in a routine action shot
does not qualify. This supersedes the ordinary-action starter batch.

## Selection rule

Choose the moment first, then the exact photograph. Record the event/date, why that particular
frame is famous, an evidence link, photographer/source, and permission to use the original and
its cut-out/blur/reveal derivatives in this game. An image of the same player, season, team or
series is not a substitute for the named moment.

The player must remain recognizable in silhouette without relying on a visible face, name or
jersey number. Keep defining gestures, limb positions, and the ball. Where another player or the
scene makes the photograph iconic (Wade's outstretched arms, Iguodala at the rim, Lue on the floor),
review whether isolating the answer player loses the moment. If it does, choose another iconic
photo or redesign that puzzle deliberately rather than silently including multiple answer players.

Use faithful masks of the actual source pixels. Do not generate replacement poses or hidden
anatomy. Technical decoding/transparency checks do not approve fame, source fidelity or gameplay.


## Selected open-license pilot — 2026-10-18

The user selected **LeBron's chalk toss first, then Dirk's fadeaway**. Both are prepared from
Keith Allison photographs under CC BY-SA 2.0, with source-faithful manual masks and actual
renderer previews. Use the [iconic-pair manifest](../content/whos-that/iconic-pair.json), not the
retired starter batch. The [preparation and renewed-search report](whos-that-photo-search-followup.md)
has source/license evidence, import commands, validation results and unresolved exact-photo leads.
Admin preview, production draft creation and phone testing remain pending.

## Other iconic targets to source

These are editorial targets, **not an upload manifest and not acquired media**. Exact frame and
permissions remain open. The references identify the moment or agency listing, not a license
already held by Know Ball. No stock-photo purchase has been made.

| Priority | Player | Photograph/moment | Source to inspect | Current status |
| --- | --- | --- | --- | --- |
| 1 | LeBron James | Game 7 Finals chase-down block on Andre Iguodala, 2016-06-19 | [NBA historical evidence](https://cms.nba.com/pr/wp-content/uploads/sites/46/2021/06/NBA-Game-7-Information-Guide.pdf); [Getty, Noah Graham/NBAE, asset 2076709378](https://www.gettyimages.com.au/detail/news-photo/lebron-james-of-the-cleveland-cavaliers-blocks-a-shot-by-news-photo/2076709378) | Verify exact frame, obtain usable original and derivative/game permissions |
| 2 | Michael Jordan | 1988 free-throw-line Slam Dunk Contest photograph, 1988-02-06 | [Walter Iooss Jr./Sports Illustrated via Getty, asset 627192818](https://www.gettyimages.com.au/detail/news-photo/slam-dunk-contest-chicago-bulls-michael-jordan-in-action-news-photo/627192818) | Inspect frame against the famous free-throw-line image; permissions unverified |
| 3 | Kobe Bryant | Arms out on the scorer's table after the 2010 Finals, 2010-06-17 | [Garrett Ellwood's account and photograph](https://andscape.com/features/kobe-vs-kobe-bryant-lakers-numbers-8-and-24-jersey-retirement/) | Exact photograph and usable original/permissions needed |
| 4 | Allen Iverson | Step-over of Tyronn Lue, 2001 Finals Game 1, 2001-06-06 | [Getty asset 638822](https://www.gettyimages.co.uk/detail/news-photo/allen-iverson-of-the-philadelphia-76ers-steps-over-tyronn-news-photo/638822); [NBA moment](https://www.nba.com/news/history-finals-moments-allen-iverson-crossover-2001) | Original/permissions needed; test whether Iverson alone preserves the pose |
| 5 | Stephen Curry | “Night night” after the late dagger against France, Paris gold-medal game, 2024-08-10 | [Getty late-game celebration candidate, asset 2165755114](https://www.gettyimages.ie/detail/news-photo/august-10-2024-usas-stephen-curry-celebrates-the-win-news-photo/2165755114) | Verify this exact frame shows the signature gesture; original/permissions needed |

These remain additional sourcing targets; the selected open-license pair above is the active pilot.
A player's most famous image need not have one objective answer.
Durant's 2017 Finals Game 3 pull-up over LeBron and Giannis's 2021 Finals Game 4 block on Ayton
are additional moment targets; neither old generic dunk qualifies as a replacement.

## LeBron alternatives explicitly requested

- **Chalk toss:** select the recognizable arms-out/up, visible chalk-cloud photograph.
  [Getty's source listings](https://www.gettyimages.com/photos/lebron-chalk-toss) show multiple years
  and frames. Exact photo/date/credit still need selection; do not invent one generic date.
- **Wade-to-LeBron dunk with Wade's arms spread:** the famous Morry Gash/AP photograph from
  Miami at Milwaukee, 2010-12-06.
  [Contemporary account identifying the photograph and photographer](https://www.sfgate.com/sports/article/Mesmerizing-Photo-of-James-and-Wade-Was-One-2386433.php).
  Preserve the defining composition in review. The date and scene matter; a different picture of
  Wade and LeBron from December 2010 is not interchangeable.
- The old `lebron-finals-2016` source was a **June 16 Game 6 dunk**, not the June 19 Game 7 block.
  It is retired, even though a transparent mask can be made from it.

## Import eligibility

The old `content/whos-that/starter-five.json` is retained as a rejected historical source record.
All five entries have `iconicReview.status: rejected`. The loader's offline check and upload
preflight reject them even when every image is present. An older review ZIP is also historical
material, not an approved content pack.

For a future upload manifest, a human reviewer must enter:

```json
"iconicReview": {
  "status": "approved",
  "moment": "Exact famous photo/moment and event",
  "reason": "Why this particular image is iconic and works as a silhouette",
  "evidenceUrl": "https://example.com/photographer-or-event-evidence"
}
```

This records a human content decision; the loader cannot determine fame from pixels. Missing,
pending, rejected or incomplete review blocks draft creation before network requests. Existing
source/credit/license fields and manual license review are still required. Approval does not
replace source permissions, cut-out review, admin preview or physical-phone/PWA testing.

Next: acquire a usable original of the chosen exact photo, record its verified metadata and
permission, make a source-faithful mask, render all three stages, then approve a draft on a phone.
Do not fall back to generic action photographs to reach the count of five or thirty.
