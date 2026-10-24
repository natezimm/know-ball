import { Component, ChangeDetectionStrategy } from '@angular/core';
import { headshot } from '../mock-data';
import { MockOption } from '../option';

@Component({
  selector: 'app-stat-quiz-mock',
  imports: [MockOption],
  template: `
    <section class="mk-game">
      <h2>Stat Quiz <span class="mk-chip">Phase 2</span></h2>
      <p class="mk-sub">
        Quick-fire stat questions against the clock. The three options are different question formats as much as
        different layouts, and a daily quiz could mix them.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="stat-quiz"
          letter="A"
          name="Multiple choice"
          bar="Stat Quiz #19"
          plus="Matches the plan as written; questions generate from templates, and the reveal teaches the real numbers."
          minus="The most familiar quiz format, so it stands out least from other trivia sites."
        >
          <div class="mk-meta"><b>Question 4 of 10</b><span class="clock">0:12</span></div>
          <div class="segs">
            @for (s of progress; track $index) {
              <i [class]="s"></i>
            }
          </div>
          <div class="mk-card">
            <span class="mk-label">2018–19 season</span>
            <p class="mk-ask">Who led the NBA in points per game?</p>
          </div>
          @for (a of answers; track a.name) {
            <div class="ans" [class.right]="a.right"><span>{{ a.name }}</span><b>{{ a.ppg }}</b></div>
          }
          <div class="mk-spacer"></div>
          <div class="mk-meta"><span>Score 2,450</span><span>+350 speed bonus</span></div>
          <span class="mk-btn">Next question</span>
        </app-mock-option>

        <app-mock-option
          game="stat-quiz"
          letter="B"
          name="Higher or lower"
          bar="Stat Quiz · Streak"
          plus="One tap per answer and an endless streak make it the most replayable; ideal for practice mode."
          minus="No fixed length, so it does not give a comparable daily score to share."
        >
          <div class="mk-meta"><span>🔥 Streak 4</span><span>Best 11</span></div>
          <p class="mk-ask">Points per game: is Curry's season higher or lower?</p>
          <div class="mk-card duel">
            <img [src]="headshot(201935)" alt="" />
            <div><b>James Harden</b><small>2018–19</small></div>
            <strong>36.1</strong>
          </div>
          <div class="mk-card duel">
            <img [src]="headshot(201939)" alt="" />
            <div><b>Stephen Curry</b><small>2015–16</small></div>
            <strong class="unknown">?</strong>
          </div>
          <div class="mk-pair">
            <span class="mk-btn">▲ Higher</span>
            <span class="mk-btn">▼ Lower</span>
          </div>
          <div class="mk-spacer"></div>
          <p class="mk-small center">One miss ends the run</p>
        </app-mock-option>

        <app-mock-option
          game="stat-quiz"
          letter="C"
          name="Rank them"
          bar="Stat Quiz #19"
          plus="Harder and more satisfying than picking one answer; partial credit keeps it fair."
          minus="Dragging is fiddly on small phones, and each question takes longer."
        >
          <div class="mk-meta"><b>Round 3 of 5</b><span class="clock">0:21</span></div>
          <p class="mk-ask">Most career points, highest first</p>
          @for (r of ranking; track r; let i = $index) {
            <div class="rank" [class.lift]="i === 1"><b>{{ i + 1 }}</b><span>{{ r }}</span><i>≡</i></div>
          }
          <p class="mk-small">Drag to reorder. Each correct spot scores.</p>
          <div class="mk-spacer"></div>
          <span class="mk-btn">Lock in order</span>
        </app-mock-option>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .clock { font-family: var(--display); color: var(--accent); font-size: 16px; }
    .segs { display: grid; grid-template-columns: repeat(10, 1fr); gap: 3px; }
    .segs i { height: 5px; border-radius: 3px; background: var(--line); }
    .segs .ok { background: var(--ok); }
    .segs .bad { background: var(--bad); }
    .segs .now { background: var(--accent); }
    .mk-card .mk-ask { margin-top: 4px; }

    .ans, .rank {
      display: flex; align-items: center; gap: 10px; padding: 11px 12px; border-radius: 10px;
      border: 1px solid var(--line); font-size: 14px;
    }
    .ans span, .rank span { flex: 1; }
    .ans b { color: var(--muted); font-weight: 500; }
    .ans.right { background: var(--ok); border-color: var(--ok); color: #fff; font-weight: 700; }
    .ans.right b { color: #fff; font-weight: 700; }

    .duel { display: flex; align-items: center; gap: 12px; }
    .duel img { width: 56px; height: 56px; border-radius: 50%; object-fit: cover; background: var(--surface-2); }
    .duel div { flex: 1; display: grid; }
    .duel small { color: var(--muted); font-size: 12px; }
    .duel strong { font-family: var(--display); font-size: 28px; }
    .unknown { color: var(--accent); }
    .center { text-align: center; }

    .rank { background: var(--surface); }
    .rank b { font-family: var(--display); color: var(--muted); width: 14px; }
    .rank i { font-style: normal; color: var(--muted); }
    .rank.lift { border-color: var(--accent); transform: translateX(8px) rotate(-1deg); box-shadow: 0 6px 16px rgb(0 0 0 / .4); }
  `,
})
export class StatQuizMock {
  protected readonly headshot = headshot;
  protected readonly progress = ['ok', 'ok', 'bad', 'now', '', '', '', '', '', ''];
  protected readonly answers = [
    { name: 'James Harden', ppg: '36.1', right: true },
    { name: 'Paul George', ppg: '28.0', right: false },
    { name: 'Giannis Antetokounmpo', ppg: '27.7', right: false },
    { name: 'Joel Embiid', ppg: '27.5', right: false },
  ];
  // Mid-drag: Malone has been pulled above Kareem.
  protected readonly ranking = ['LeBron James', 'Karl Malone', 'Kareem Abdul-Jabbar', 'Kobe Bryant'];
}
