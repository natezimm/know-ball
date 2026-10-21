# Future games

Updated October 21, 2026. The active release contains **Spordle, Scouting Report and Career Path**.
**Numbers Don't Lie** (formerly Stat Quiz) is the proposed fourth game. Its
[implementation plan](numbers-dont-lie-implementation-plan.md) covers the recommended five-question,
four-sport shared daily quiz, customizable practice with remembered league selection, content proof and
release gates. It is not implemented or a three-game launch requirement.

## Who's That Sportsman? — deferred

The concept is to recognize a player from an iconic photograph: start with a silhouette, then unlock
the year, team and blurred photograph after misses, revealing the original and its credit at the end.

The five-photo pilot (LeBron chalk toss, Dirk fadeaway, Dr. J dunk, Kobe rim hang and Wilt holding “100”)
was rejected on visual quality. The cutouts did not meet the standard needed for an enjoyable game.
The implementation, admin upload workflow, renderer, content manifests and dedicated tests were removed
from the active project. Do not treat prior automated image checks as editorial approval.

Revisit only when:

- Real iconic photographs or signature moments are available with documented permission or an appropriate license.
- Faithful cutouts preserve the photographed pose and produce clean, recognizable silhouettes.
- A small pilot passes human review for appearance and difficulty on real phones before building a large queue.
- Ongoing sourcing and authoring effort is practical for a solo project.

Previous implementation and research are recoverable in
[the pre-removal repository](https://github.com/natezimm/know-ball/tree/96344829f0c6c4c11b2a1e02da8fb6ade0185b90).
The separately saved photo packs remain available. Existing database rows, the reserved `GameType.Silhouette = 2`
value and legacy media storage are preserved for compatibility; there is no active photo API or admin route.
Reintroducing the game would require a fresh implementation decision and review of the earlier rights evidence.

## Fourth-game plan and alternatives

Numbers Don't Lie is selected for planning, with NBA, NFL, MLB and NHL as the proposed first leagues.
Start with the 24-question source proof and Trading Card prototype described in its plan before building
or enabling the full game. The remaining candidates are unscheduled alternatives:

| Candidate | What needs proving |
| --- | --- |
| Numbers Don't Lie (formerly Stat Quiz) | Four-sport source proof, unambiguous reviewed questions, an enjoyable short daily quiz and sustainable content supply |
| Geo Sports | Arena/birthplace coordinates, map interaction on phones and a useful scoring model |
| Sportle | Distinct identity from Spordle, season-stat clues and automated content quality |
| Sound It Out | A sustainable licensed image collection and new authoring tools |
| Who's the GOAT? | Career/award/championship data, clear rules and an engaging ranking loop |

The fourth game should use its own identifier; do not repurpose the reserved legacy photo-game value.
