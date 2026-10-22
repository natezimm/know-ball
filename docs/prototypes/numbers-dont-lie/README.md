# Numbers Don't Lie: private design proof

This is delivery PR 1 from the [implementation plan](../../numbers-dont-lie-implementation-plan.md).
It is a static product/content review, outside Angular assets, routing, service-worker caching and the public home page.
The three existing games remain the release. Do not copy this folder into `public`, `wwwroot` or a production bundle:
**the JSON intentionally contains all answers.** Production scoring and answer filtering belong to the later saved API.

## Run

From the repository root, install the existing web dependencies once (`npm ci --prefix web/know-ball`), then:

```bash
python3 -m http.server 8090 --bind 127.0.0.1
```

Open <http://localhost:8090/docs/prototypes/numbers-dont-lie/index.html>.
The prototype uses the same Fontsource packages as the app, served locally from `web/know-ball/node_modules`.
It makes no statistics-provider requests during play. Use an HTTP server, not `file://`, for its JSON/modules.

```bash
node --test docs/prototypes/numbers-dont-lie/model.test.mjs
node docs/prototypes/numbers-dont-lie/browser-check.mjs
```

The browser check starts its own loopback server and uses the app's Playwright installation.
Run `cd web/know-ball && npx playwright install chromium` if needed. Optional `PROTOTYPE_CHROMIUM`
selects an existing executable. `PROTOTYPE_SCREENSHOT` saves a mobile-width image.
`PROTOTYPE_PLAYWRIGHT_MODULE` can select an already installed Playwright module in a constrained runner.

## What to review

- Daily preview: a fixed five-question sample, one per league plus an NBA bonus; identical regardless of practice selection.
- Practice: all 15 nonempty league combinations, evenly allocated five-question rounds and remembered choices.
- Native radio choices, explicit Lock in answer, one submission per question, feedback before Next, correct count and review.
- Both comparison and exact-value prompts; source credits and all four supporting values after each answer.
- A cancelled abandon prompt preserves the current feedback. Leaving an answered unfinished round requires confirmation.

Reload starts over; only league preferences persist. There is no backend, durable progress, real calendar rollover,
statistics, archive, sharing, daily queue reservation or production feature gate in this prototype.
Practice deliberately reuses the small review pool, so it can overlap the daily sample. It does not demonstrate
the production no-spoilers reservation policy or a sustainable difficulty mix for every filter.

## Review worksheet

For each question in [the source review](../../numbers-dont-lie-data-sources.md), record accuracy, clarity,
whether the distractors feel plausible, perceived difficulty and any wording correction.
All difficulty labels are hypotheses; automated checks do not establish that a quiz is fun.

- [ ] Human review of all 24 questions and both templates.
- [ ] Play one daily and one single-league practice round; record completion time and enjoyment.
- [ ] Try a mixed-league round, change selections and reload the setup.
- [ ] Physical iPhone/Safari and Android/Chrome: keyboard focus, zoom, long names, safe area, scrolling and abandonment.
- [ ] Confirm five untimed questions and four choices feel right before treating these defaults as final.
- [ ] Resolve production source permissions and repeatable NHL ingestion before advancing the content approval gate.

Browser automation covers Chromium desktop/mobile widths, not actual phones, Safari, screen readers or installed PWAs.
See [data-source evidence and open gates](../../numbers-dont-lie-data-sources.md).
