# Numbers Don't Lie

The game serves five multiple-choice questions per round across the NBA, NFL, MLB and NHL. Players can join the shared
daily round or filter a practice round by league, topic and year. The 262-question bank is embedded in
`src/KnowBall.Infrastructure/Data/numbers-questions.json`.

| Note | Covers |
| --- | --- |
| [gameplay.md](gameplay.md) | How the game plays, what the server stores and the editorial standard for questions |
| [practice-replay.md](practice-replay.md) | Practice filters, 30-day repeat avoidance and the explicit repeat opt-in |
| [catalogue-preflight.md](catalogue-preflight.md) | Offline validation of a question catalogue (`--validate-numbers`) |
| [content-storage.md](content-storage.md) | Approved catalogues and immutable staged quizzes |
| [scheduling.md](scheduling.md) | The daily policy and offline supply simulation (`--simulate-numbers`) |

The approved-content queue from the last two notes is built but disabled (`NumbersDontLie:QueueEnabled`); the game
draws from the embedded bank. The original plan and source proof are in [the archive](../archive/README.md), and
the browser prototype is in [docs/prototypes/numbers-dont-lie](../prototypes/numbers-dont-lie/README.md).
