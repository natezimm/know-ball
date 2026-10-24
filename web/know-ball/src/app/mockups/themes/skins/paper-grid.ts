import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** Off-white paper, black rules, square corners and mono labels, after sudoku.nathanzimmerman.com. */
@Component({
  selector: 'app-theme-paper-grid',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-paper' },
  template: `<app-theme-screen />`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-paper .ts {
      --mono: "IBM Plex Mono", "Courier New", monospace;
      background: #efefe6; color: #000; font-family: "Space Grotesk", Arial, sans-serif;
    }
    .th-paper .ts-bar { margin: 0 14px; padding: 11px 0; border-bottom: 2px solid #000; }
    .th-paper .ts-brand { font-size: 20px; letter-spacing: -.06em; text-transform: lowercase; gap: 9px; }
    .th-paper .ts-mark { width: 20px; height: 20px; box-sizing: border-box; background: #ffdb3b; border: 2px solid #000; box-shadow: 2px 2px 0 #000; }
    .th-paper .ts-nav span { font: 500 10px var(--mono); text-transform: uppercase; letter-spacing: .06em; }
    .th-paper .ts-nav .on { background: #000; color: #fff; }
    .th-paper .ts-title { font-size: 32px; letter-spacing: -.07em; text-transform: lowercase; }
    .th-paper .ts-btn, .th-paper .ts-chip, .th-paper .ts-key { background: #fff; border: 2px solid #000; box-shadow: 2px 2px 0 #000; font-weight: 600; }
    .th-paper .ts-btn { box-shadow: 3px 3px 0 #000; padding: 5px 10px; }
    .th-paper .ts-chip { padding: 3px 10px; }
    .th-paper .ts-chip.on { background: #3bff9d; }
    .th-paper .ts-seg { gap: 0; }
    .th-paper .ts-seg .ts-label { margin-right: 8px; }
    .th-paper .ts-seg i { background: #fff; border: 2px solid #000; }
    .th-paper .ts-seg i + i { margin-left: -2px; }
    .th-paper .ts-seg .on { background: #ffdb3b; }
    .th-paper .ts-label, .th-paper .ts-status { font: 500 9px var(--mono); letter-spacing: .12em; text-transform: uppercase; color: #4a4a4a; }
    .th-paper .ts-status { font-size: 10px; color: #000; }
    .th-paper .ts-grid { gap: 0; width: fit-content; margin: 0 auto; background: #fff; border: 2px solid #000; box-shadow: 5px 5px 0 #000; }
    .th-paper .ts-row { gap: 0; grid-template-columns: repeat(5, 48px); }
    .th-paper .ts-tile { height: 48px; border: 1px solid #000; font-weight: 500; font-size: 26px; }
    .th-paper .ts-tile.typed { background: #c2ff66; }
    .th-paper .ts-tile.correct { background: #3bff9d; }
    .th-paper .ts-tile.present { background: #ffdb3b; }
    .th-paper .ts-tile.absent { background: #000; color: #fff; }
    .th-paper .ts-key.correct { background: #3bff9d; }
    .th-paper .ts-key.present { background: #ffdb3b; }
    .th-paper .ts-key.absent { background: #000; color: #fff; box-shadow: none; }
  `,
})
export class PaperGridTheme {}
