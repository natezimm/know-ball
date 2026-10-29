# Numbers Don't Lie

The fourth game: five multiple-choice questions on stats, awards, champions and seasons across the NBA, NFL, MLB
and NHL, as a shared daily round or a filtered practice round. It is a playable beta with a 96-question bank
(24 per league) embedded in `src/KnowBall.Infrastructure/Data/numbers-beta.json`.

| Note | Covers |
| --- | --- |
| [live-beta.md](live-beta.md) | How the beta plays, what the server stores and the editorial standard for questions |
| [practice-replay.md](practice-replay.md) | Practice filters, 30-day repeat avoidance and the explicit repeat opt-in |
| [catalogue-preflight.md](catalogue-preflight.md) | Offline validation of a question catalogue (`--validate-numbers`) |
| [content-storage.md](content-storage.md) | Approved catalogues and immutable staged quizzes |
| [scheduling.md](scheduling.md) | The daily policy and offline supply simulation (`--simulate-numbers`) |

The approved-content queue from the last two notes is built but disabled (`NumbersDontLie:QueueEnabled`); the beta
draws from the embedded bank. The original plan and source proof are in [the archive](../archive/README.md), and
the browser prototype is in [docs/prototypes/numbers-dont-lie](../prototypes/numbers-dont-lie/README.md).
