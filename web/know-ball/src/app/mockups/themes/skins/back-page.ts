import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/**
 * The puzzle corner of a newspaper's sports page: blackletter masthead, crossword grid,
 * correct letters highlighted, misplaced ones circled in red pen, wrong ones blacked out.
 */
@Component({
  selector: 'app-theme-back-page',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-news' },
  template: `<app-theme-screen />`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-news .ts {
      --ink: #1a1a1a; --cell: #fffdf6; --pen: #c0392b; --marker: #fff06a;
      background: #f4f0e4; color: var(--ink); font-family: "Libre Baskerville", Georgia, serif;
    }
    .th-news .ts-bar { flex-direction: column; gap: 5px; margin: 0 14px; padding: 12px 0 5px; border-bottom: 3px double var(--ink); }
    .th-news .ts-brand { font: 700 28px/1 "UnifrakturCook", "Old English Text MT", serif; }
    .th-news .ts-mark { display: none; }
    .th-news .ts-nav {
      align-self: stretch; justify-content: center; gap: 16px; padding-top: 4px; border-top: 1px solid var(--ink);
      font-size: 8px; text-transform: uppercase; letter-spacing: .16em;
    }
    .th-news .ts-nav span { padding: 0; }
    .th-news .ts-nav .on { font-weight: 700; }
    .th-news .ts-title { font: italic 900 36px/1 "Playfair Display", Georgia, serif; }
    .th-news .ts-btn { border: 1px solid var(--ink); background: var(--cell); font-size: 9px; text-transform: uppercase; letter-spacing: .08em; padding: 5px 8px; }
    .th-news .ts-btn:first-child { background: var(--ink); color: #f4f0e4; }
    .th-news .ts-toolbar { padding-bottom: 8px; border-bottom: 1px solid var(--ink); }
    .th-news .ts-seg i { border: 1px solid var(--ink); background: var(--cell); font-family: "Playfair Display", Georgia, serif; font-size: 15px; }
    .th-news .ts-seg .on { background: var(--ink); color: #f4f0e4; }
    .th-news .ts-label { font-weight: 700; font-size: 9px; }
    .th-news .ts-status { font-style: italic; }
    .th-news .ts-chip { padding: 1px 4px; font-size: 13px; font-style: italic; border-bottom: 1px dotted var(--ink); }
    .th-news .ts-chip.on { font-style: normal; font-weight: 700; border: 0; background: linear-gradient(transparent 18%, var(--marker) 18% 90%, transparent 90%); }
    .th-news .ts-grid { gap: 0; width: fit-content; margin: 0 auto; border: 2px solid var(--ink); counter-reset: clue; }
    .th-news .ts-row { gap: 0; grid-template-columns: repeat(5, 48px); counter-increment: clue; }
    .th-news .ts-tile { height: 48px; border: 1px solid var(--ink); background: var(--cell); font: 800 26px/1 "Playfair Display", Georgia, serif; }
    .th-news .ts-tile:first-child::before {
      content: counter(clue); position: absolute; top: 2px; left: 3px; font: 400 8px/1 "Libre Baskerville", Georgia, serif;
    }
    .th-news .ts-tile.correct { background: var(--marker); }
    .th-news .ts-tile.present::after {
      content: ""; position: absolute; inset: 5px 4px; border: 2px solid var(--pen); border-radius: 50%; transform: rotate(-10deg);
    }
    .th-news .ts-tile.absent { background: var(--ink); color: #777; }
    .th-news .ts-keys { gap: 8px; }
    .th-news .ts-key {
      max-width: 27px; height: 27px; border: 1px solid var(--ink); border-radius: 50%; background: var(--cell);
      box-shadow: 0 2px 0 var(--ink); font-size: 12px;
    }
    .th-news .ts-key.wide { max-width: 46px; border-radius: 14px; font-size: 7px; letter-spacing: .04em; }
    .th-news .ts-key.correct { background: var(--marker); }
    .th-news .ts-key.present { box-shadow: 0 2px 0 var(--ink), inset 0 0 0 2px var(--pen); }
    .th-news .ts-key.absent { background: var(--ink); color: #777; }
  `,
})
export class BackPageTheme {}
