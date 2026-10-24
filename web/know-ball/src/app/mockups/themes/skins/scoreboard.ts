import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** An arena scoreboard: black panel, dot-matrix letters that light up amber, green or stay dark. */
@Component({
  selector: 'app-theme-scoreboard',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-led' },
  template: `<app-theme-screen />`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-led .ts {
      --glow: 0 0 7px currentColor;
      background: #060606 radial-gradient(#1c1c1c 1px, transparent 1.3px) 0 0 / 6px 6px;
      color: #ffb000; font-family: "Share Tech Mono", ui-monospace, monospace;
    }
    .th-led .ts-bar { background: #0b0b0b; border-bottom: 2px solid #1f1f1f; }
    .th-led .ts-brand, .th-led .ts-title, .th-led .ts-tile, .th-led .ts-seg i, .th-led .ts-status {
      font-family: "Doto", "Share Tech Mono", monospace; font-weight: 900; text-transform: uppercase;
    }
    .th-led .ts-brand { font-size: 18px; letter-spacing: .06em; text-shadow: var(--glow); }
    .th-led .ts-mark { width: 9px; height: 9px; border-radius: 50%; background: #ff3b30; box-shadow: 0 0 8px #ff3b30; }
    .th-led .ts-nav span { color: #7a5600; text-transform: uppercase; }
    .th-led .ts-nav .on { color: #ffb000; text-shadow: var(--glow); }
    .th-led .ts-title { font-size: 36px; letter-spacing: .04em; text-shadow: var(--glow); }
    .th-led .ts-btn, .th-led .ts-chip { border: 1px solid #333; border-radius: 3px; background: #0d0d0d; text-transform: uppercase; font-size: 11px; }
    .th-led .ts-chip.on { color: #39ff14; border-color: #1f6b12; text-shadow: var(--glow); }
    .th-led .ts-seg i { border: 1px solid #333; border-radius: 3px; background: #0d0d0d; color: #7a5600; font-size: 18px; }
    .th-led .ts-seg .on { color: #ffb000; border-color: #ffb000; text-shadow: var(--glow); }
    .th-led .ts-label { color: #8a8a8a; }
    .th-led .ts-status { color: #ff3b30; font-size: 15px; text-shadow: var(--glow); }
    .th-led .ts-tile { background: #0c0c0c; border: 1px solid #262626; border-radius: 4px; box-shadow: inset 0 0 10px #000; font-size: 36px; }
    .th-led .ts-tile.typed { color: #fff; border-color: #666; text-shadow: var(--glow); }
    .th-led .ts-tile.correct { color: #39ff14; border-color: #1f6b12; background: #061606; text-shadow: var(--glow); }
    .th-led .ts-tile.present { color: #ffb000; border-color: #7a5600; background: #171000; text-shadow: var(--glow); }
    .th-led .ts-tile.absent { color: #3d3d3d; }
    .th-led .ts-key { background: #101010; border: 1px solid #2a2a2a; border-radius: 3px; color: #c98a00; font-weight: 400; font-size: 15px; }
    .th-led .ts-key.wide { font-size: 9px; }
    .th-led .ts-key.correct { color: #39ff14; border-color: #1f6b12; text-shadow: var(--glow); }
    .th-led .ts-key.present { color: #ffb000; border-color: #ffb000; text-shadow: var(--glow); }
    .th-led .ts-key.absent { color: #3a3a3a; border-color: #1a1a1a; }
  `,
})
export class ScoreboardTheme {}
