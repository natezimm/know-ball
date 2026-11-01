# AGENTS.md

Guidance for coding agents working in this repository. `CLAUDE.md` imports this file, so edit this one. The
README covers what Know Ball is; [docs/architecture.md](docs/architecture.md) has the diagrams.

Know Ball is a set of daily sports trivia games: an ASP.NET Core (.NET 10) API that also serves the built
Angular 22 app, a worker that imports player data and queues puzzles, and PostgreSQL. All three ship in one
Docker image.

## Commands

Run from the repository root unless a line says otherwise.

| Task | Command |
| --- | --- |
| Build everything | `dotnet build KnowBall.sln` |
| Unit tests | `dotnet test tests/KnowBall.UnitTests` |
| Integration tests on SQLite | `dotnet test tests/KnowBall.IntegrationTests` |
| Integration tests on Postgres | `TRIVIA_TEST_POSTGRES="Host=localhost;Port=5432;Database=sports_trivia;Username=trivia;Password=trivia" dotnet test tests/KnowBall.IntegrationTests` |
| One test class | `dotnet test tests/KnowBall.UnitTests --filter "FullyQualifiedName~GameServiceTests"` |
| Web unit tests, in `web/know-ball` | `npm test` |
| Web production build, in `web/know-ball` | `npm run build` |
| Browser tests, in `web/know-ball` | `npm run e2e` (builds first; needs the .NET SDK; `npx playwright install chromium` once) |
| Postgres for local runs | `docker compose up -d db` |
| Load data offline | `dotnet run --project src/KnowBall.Importer -- --once --source seed --no-history` |
| API on port 5080 | `dotnet run --project src/KnowBall.Api` |
| Web dev server on port 4200, in `web/know-ball` | `npm start` (proxies `/api` to 5080) |
| Whole site in Docker on port 8080 | `docker compose --profile site up -d --build` |
| Everything CI runs, plus Docker | `./scripts/verify.sh` |
| New Postgres migration | `dotnet tool install dotnet-ef --tool-path .tools`, then `.tools/dotnet-ef migrations add <Name> --project src/KnowBall.Infrastructure --startup-project src/KnowBall.Infrastructure --output-dir Persistence/Migrations` |

Without Docker, point the API and importer at SQLite:
`Database__Provider=Sqlite ConnectionStrings__Trivia="Data Source=$PWD/artifacts/trivia-dev.db"`.

The Angular CLI needs Node 22.22.3+ or 24.15+; CI uses Node 24. There is no linter, and Prettier is not enforced,
so do not reformat files you are not otherwise changing.

## Architecture

- **Layers point inward.** `Api` and `Importer` reference `Infrastructure`, which references `Application`, which
  references `Domain`. Game rules, the scheduler and import logic belong in `Application` and reach the database
  only through `ITriviaDb`. Provider code, data source clients and migrations belong in `Infrastructure`.
  Numbers Don't Lie's `NumbersPlayService` is the one service in `Infrastructure`.
- **The daily-game engine.** `GameService` and `PuzzleScheduler` run every "guess the player" game. A new one
  implements `IGame` (`Application/Games/GameModels.cs`) and is registered as an `IGame` in
  `Application/DependencyInjection.cs`, which feeds `GameRegistry`. The day is always the Eastern date from
  `PuzzleCalendar`; never use the server's local date.
- **Answers never leave early.** `Puzzle.PayloadJson` is public and `Puzzle.SecretJson` is not. Return the answer
  only once a result is won or lost, and keep daily puzzles unplayable before their date
  ([ADR 0002](docs/adr/0002-answers-stay-on-the-server.md)).
- **Guesses are idempotent.** Keep `requestId` handling and the compare-and-swap in `GameService.GuessAsync`
  when changing guesses ([ADR 0004](docs/adr/0004-idempotent-guesses.md)).
- **Players are anonymous.** The browser's `X-Anon-Id` is identity, not authentication. Do not build features
  that treat it as a secret ([ADR 0003](docs/adr/0003-anonymous-players.md)).
- **The importer is defensive.** Keep imports repeatable (CI runs it twice and expects no changes) and keep the
  half-roster guard in `PlayerImportService` ([ADR 0005](docs/adr/0005-defensive-nightly-import.md)).
- **Time and randomness are injected** (`TimeProvider`, `PuzzleScheduler.Random`); tests use
  `FakeTimeProvider` and fixed seeds rather than real clocks.
- **Two databases, one model.** Postgres uses EF Core migrations applied when the API starts. SQLite uses
  `EnsureCreated` with a schema version number in `DatabaseInitializer`; bump it when the model changes, and add
  a Postgres migration in the same change. Never edit an existing migration.
- **Configuration** is read through options or inside DI factories. Rate limits are in
  `Api/Endpoints/RateLimits.cs`; [docs/api.md](docs/api.md) and [docs/importer.md](docs/importer.md) list the
  settings, so update them when a setting or endpoint changes.

The web app (`web/know-ball/src/app/`) is standalone and zoneless: `core/` holds the API client, the anonymous id
and shared stores, `shared/` the game shell and player search, `games/` one folder per game, and `mockups/` the
design lab for games not built yet. Every route gets the same `index.html`, so `core/page-meta.ts` sets each
page's canonical link, description and `noindex` from route `data`; keep `public/sitemap.xml` and
`public/robots.txt` in step with the indexed routes. Components keep templates and styles inline. Colours and fonts are CSS
variables in `src/styles.css`, with shared `kb-` classes there (`kb-btn`, `kb-dialog`); components use those
rather than raw values or unstyled controls.

## Tests

- Backend behaviour gets a unit test in `tests/KnowBall.UnitTests` (SQLite in memory, fixtures in
  `tests/fixtures`) and, for anything visible over HTTP, an integration test that runs on both SQLite and
  Postgres.
- The browser tests in `web/know-ball/e2e` start their own site (`e2e/serve.sh`) on port 5180 with a fresh SQLite
  database loaded offline, and win and lose each game. Requests that leave the site are blocked, so headshots
  and logos show their fallbacks there.
- CI (`.github/workflows/ci.yml`) runs backend, web, e2e and Docker jobs on every pull request; a push to `main`
  then publishes the image and deploys it.

## Project rules

- **Changes reach `main` through pull requests**, and `main` deploys to production automatically once CI passes.
- **Player data, headshots and logos belong to their sources.** Do not commit downloaded headshots or logos;
  link them as the app does. See [docs/research.md](docs/research.md).
- **Keep the README a storefront.** Dated history and plans go in `docs/archive/` or the pull request, not the
  README.
- **Retired features stay retired.** The photo game (Who's That Sportsman?) was removed; its database values,
  such as `GameType.Silhouette` and the `photos` volume, are kept for compatibility. Do not reuse them.
