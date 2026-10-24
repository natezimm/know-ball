import { Component, ChangeDetectionStrategy } from '@angular/core';
import { MockOption } from '../option';

@Component({
  selector: 'app-goat-mock',
  imports: [MockOption],
  template: `
    <section class="mk-game">
      <h2>Who's the GOAT? <span class="mk-chip">Phase 3</span></h2>
      <p class="mk-sub">
        Not a daily puzzle: weight what matters to you and the all-time ranking reorders live. Save it, share it and
        compare with everyone else's.
      </p>

      <div class="mk-row">
        <app-mock-option
          game="goat"
          letter="A"
          name="Sliders and live list"
          bar="Who's the GOAT?"
          plus="Direct and playful: drag a slider and watch names swap places. Matches the plan as written."
          minus="Six independent sliders are hard to compare, since everything maxed out equals everything halved."
        >
          @for (w of weights; track w.name) {
            <div class="slider">
              <span>{{ w.name }}</span>
              <div class="rail"><i [style.width.%]="w.value"></i><u [style.left.%]="w.value"></u></div>
              <b>{{ w.value }}</b>
            </div>
          }
          <span class="mk-label">Your ranking</span>
          @for (p of ranking; track p.name; let i = $index) {
            <div class="row">
              <b>{{ i + 1 }}</b><span>{{ p.name }}</span>
              <div class="score"><i [style.width.%]="p.score"></i></div>
              <small>{{ p.score }}</small>
            </div>
          }
          <div class="mk-spacer"></div>
          <span class="mk-btn">Share my ranking</span>
        </app-mock-option>

        <app-mock-option
          game="goat"
          letter="B"
          name="100-point budget"
          bar="Who's the GOAT?"
          plus="Forces real trade-offs, so every formula is comparable; presets give a one-tap starting point."
          minus="Steppers are slower than sliders, and the budget rule takes a line of explaining."
        >
          <div class="presets">
            <span class="mk-chip on">Ring culture</span>
            <span class="mk-chip">Stat sheet</span>
            <span class="mk-chip">Peak years</span>
            <span class="mk-chip">Iron man</span>
          </div>
          <div class="mk-meta"><span>100 points to spend</span><b>0 left</b></div>
          <div class="stack">
            @for (w of budget; track w.name; let i = $index) {
              <i [style.flex-grow]="w.value" [style.opacity]="1 - i * 0.15"></i>
            }
          </div>
          <div class="steppers">
            @for (w of budget; track w.name) {
              <div class="step"><span>{{ w.name }}</span><u>−</u><b>{{ w.value }}</b><u>+</u></div>
            }
          </div>
          <div class="mk-spacer"></div>
          <div class="podium">
            @for (p of podium; track p.name) {
              <div [class]="'place p' + p.place">
                <span class="avatar">{{ p.initials }}</span>
                <small>{{ p.name }}</small>
                <b>{{ p.place }}</b>
              </div>
            }
          </div>
        </app-mock-option>

        <app-mock-option
          game="goat"
          letter="C"
          name="You vs. the crowd"
          bar="Who's the GOAT?"
          plus="Leads with the argument, which is the fun part: where you and everyone else disagree."
          minus="Needs enough saved rankings to have a crowd, so it only works after launch; editing is a second screen."
        >
          <div class="mk-card">
            <div class="mk-meta"><span class="mk-label">Your formula</span><span class="mk-chip">Edit</span></div>
            <p class="formula">Peak 90 · Rings 80 · MVPs 70 · Points 60 · All-NBA 40 · Longevity 35</p>
          </div>
          <div class="match"><strong>74%</strong><span>match with the average ranking</span></div>
          <div class="versus head"><span></span><span>You</span><span>Everyone</span></div>
          @for (r of versus; track r.you; let i = $index) {
            <div class="versus">
              <b>{{ i + 1 }}</b>
              <span>{{ r.you }} <em [class]="r.move">{{ r.delta }}</em></span>
              <span class="crowd">{{ r.crowd }}</span>
            </div>
          }
          <div class="mk-spacer"></div>
          <span class="mk-btn">Share my list</span>
        </app-mock-option>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .slider { display: grid; grid-template-columns: 74px 1fr 26px; align-items: center; gap: 10px; font-size: 13px; }
    .slider span { color: var(--muted); }
    .slider b { text-align: right; font-variant-numeric: tabular-nums; }
    .rail { position: relative; height: 4px; border-radius: 2px; background: var(--line); }
    .rail i { display: block; height: 100%; border-radius: 2px; background: var(--accent); }
    .rail u {
      position: absolute; top: 50%; width: 16px; height: 16px; margin: -8px 0 0 -8px; border-radius: 50%;
      background: var(--text); border: 3px solid var(--accent);
    }
    .row { display: grid; grid-template-columns: 14px 142px 1fr 22px; align-items: center; gap: 8px; font-size: 13px; }
    .row b, .versus b { font-family: var(--display); color: var(--muted); }
    .row small { color: var(--muted); text-align: right; }
    .score { height: 8px; border-radius: 4px; background: var(--surface-2); }
    .score i { display: block; height: 100%; border-radius: 4px; background: var(--accent); }

    .presets { display: flex; flex-wrap: wrap; gap: 6px; }
    .stack { display: flex; gap: 2px; height: 10px; }
    .stack i { flex: 0 0 0; border-radius: 3px; background: var(--accent); }
    .steppers { display: grid; gap: 6px; }
    .step { display: grid; grid-template-columns: 1fr 30px 30px 30px; align-items: center; gap: 4px; font-size: 13px; }
    .step b { text-align: center; }
    .step u {
      height: 28px; border-radius: 8px; border: 1px solid var(--line); display: grid; place-items: center;
      text-decoration: none; color: var(--muted);
    }
    .podium { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px; align-items: end; }
    .place { display: grid; justify-items: center; gap: 4px; text-align: center; }
    .place small { font-size: 11px; line-height: 1.2; }
    .place b {
      width: 100%; border-radius: 8px 8px 0 0; background: var(--surface-2); font-family: var(--display);
      font-size: 18px; padding-top: 4px; height: 34px;
    }
    .p1 { order: 2; }
    .p1 b { height: 62px; background: var(--accent); color: var(--accent-ink); }
    .p2 { order: 1; }
    .p2 b { height: 46px; }
    .p3 { order: 3; }
    .avatar {
      width: 40px; height: 40px; border-radius: 50%; background: var(--surface-2); border: 1px solid var(--line);
      display: grid; place-items: center; font-family: var(--display); font-size: 14px;
    }

    .formula { margin: 6px 0 0; font-size: 12px; color: var(--muted); }
    .match { display: flex; align-items: baseline; gap: 8px; font-size: 13px; color: var(--muted); }
    .match strong { font-family: var(--display); font-size: 30px; color: var(--accent); }
    .versus { display: grid; grid-template-columns: 14px 1fr 124px; gap: 8px; align-items: center; font-size: 13px; }
    .versus.head { font-size: 10px; text-transform: uppercase; letter-spacing: .08em; color: var(--muted); }
    .versus:not(.head) { padding: 7px 0; border-top: 1px solid var(--line); }
    .versus em { font-style: normal; font-size: 11px; color: var(--muted); }
    .versus .up { color: var(--ok); }
    .versus .down { color: var(--bad); }
    .crowd { color: var(--muted); font-size: 12px; }
  `,
})
export class GoatMock {
  protected readonly weights = [
    { name: 'Points', value: 60 },
    { name: 'Rings', value: 80 },
    { name: 'MVPs', value: 70 },
    { name: 'All-NBA', value: 40 },
    { name: 'Peak', value: 90 },
    { name: 'Longevity', value: 35 },
  ];
  protected readonly ranking = [
    { name: 'Michael Jordan', score: 96 },
    { name: 'LeBron James', score: 95 },
    { name: 'Kareem Abdul-Jabbar', score: 90 },
    { name: 'Bill Russell', score: 84 },
    { name: 'Magic Johnson', score: 82 },
  ];

  // The "Ring culture" preset.
  protected readonly budget = [
    { name: 'Rings', value: 35 },
    { name: 'MVPs', value: 20 },
    { name: 'Peak', value: 20 },
    { name: 'Points', value: 10 },
    { name: 'All-NBA', value: 10 },
    { name: 'Longevity', value: 5 },
  ];
  protected readonly podium = [
    { place: 1, name: 'Bill Russell', initials: 'BR' },
    { place: 2, name: 'Michael Jordan', initials: 'MJ' },
    { place: 3, name: 'Kareem Abdul-Jabbar', initials: 'KA' },
  ];

  protected readonly versus = [
    { you: 'LeBron James', delta: '▲1', move: 'up', crowd: 'Michael Jordan' },
    { you: 'Michael Jordan', delta: '▼1', move: 'down', crowd: 'LeBron James' },
    { you: 'Kareem Abdul-Jabbar', delta: '', move: '', crowd: 'Kareem Abdul-Jabbar' },
    { you: 'Tim Duncan', delta: '▲3', move: 'up', crowd: 'Magic Johnson' },
    { you: 'Bill Russell', delta: '', move: '', crowd: 'Bill Russell' },
    { you: 'Magic Johnson', delta: '▼2', move: 'down', crowd: 'Kobe Bryant' },
  ];
}
