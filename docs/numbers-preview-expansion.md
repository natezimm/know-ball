# Expanded Numbers Don't Lie preview

October 24, 2026: user authorized the broader preview iteration.

The hosted preview now includes 56 facts: the original 24 statistical questions plus
12 awards, 12 championship teams, and 8 season-identification questions. All four
leagues have 14 questions. Historical team names are retained. NFL championship
prompts specify the season, rather than the calendar year of the Super Bowl.
Each question links its supporting source. These additions do not resolve the
production data sourcing/reuse gates in the implementation plan.

Practice offers optional topic and era multiselect filters. Era uses season start
year. League preferences persist; topic and era choices last for this visit.
Insufficient combinations display an error instead of silently broadening filters.
Rounds favor unseen questions and varied topics, balance selected leagues, and
order questions by provisional difficulty. Difficulty still needs human calibration.
Replay retains filters. Answer positions are shuffled.

Daily questions are deterministic by Eastern date and catalogue version, with all
four leagues represented. Completed daily results are retained in this browser and
cannot be replayed for a new score through the UI. Practice has a separate best
score and recent-question history. Daily completion streaks count participation,
including a zero score. Share text contains score squares, never answers.

This is still a client-side preview: no server verification, cross-device sync,
immutable published daily snapshots, production stats integration or private answer
bank. Unfinished rounds reset on reload. Clearing storage resets progress; blocked
storage allows play but cannot retain it. Catalogue updates can change daily sets.
The existing production implementation plan remains necessary before launch.

Validation: model tests cover catalogue integrity, all 15 league combinations,
topic/era filtering, repeat avoidance, date rollover and streaks. Browser checks
cover daily/practice scoring, completed daily reload, feedback, blocked storage,
abandon confirmation and mobile widths. Existing hosted E2E assertions were updated.
