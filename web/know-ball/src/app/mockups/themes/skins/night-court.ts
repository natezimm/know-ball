import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';
import { ThemeScreen } from '../theme-screen';

/** The look the app has today: dark navy, amber accent, rounded tiles. */
@Component({
  selector: 'app-theme-night-court',
  imports: [ThemeScreen],
  encapsulation: ViewEncapsulation.None,
  host: { class: 'th-host th-now' },
  template: `<app-theme-screen />`,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-now .ts { background: #0f1720; color: #eef2f6; font-family: var(--font); }
    .th-now .ts-bar { border-bottom: 1px solid #2c3b4b; }
    .th-now .ts-brand, .th-now .ts-title, .th-now .ts-tile { font-family: var(--display); text-transform: uppercase; }
    .th-now .ts-brand { letter-spacing: .12em; }
    .th-now .ts-title { letter-spacing: .04em; }
    .th-now .ts-mark { border-radius: 5px; background: #f5a524; box-shadow: inset 0 0 0 3px #0f1720, 0 0 0 2px #f5a524; }
    .th-now .ts-nav span { color: #93a3b5; border-radius: 999px; }
    .th-now .ts-nav .on { background: #f5a524; color: #1b1300; font-weight: 600; }
    .th-now .ts-btn, .th-now .ts-chip { border: 1px solid #2c3b4b; border-radius: 999px; background: #17212c; }
    .th-now .ts-chip.on { border-color: #f5a524; color: #f5a524; }
    .th-now .ts-seg i { border: 1px solid #2c3b4b; border-radius: 8px; color: #93a3b5; }
    .th-now .ts-seg .on { background: #f5a524; border-color: #f5a524; color: #1b1300; }
    .th-now .ts-label, .th-now .ts-status { color: #93a3b5; }
    .th-now .ts-tile { border: 2px solid #2c3b4b; border-radius: 8px; background: #17212c; }
    .th-now .ts-tile.typed { border-color: #93a3b5; }
    .th-now .ts-tile.correct { background: #2ea043; border-color: #2ea043; }
    .th-now .ts-tile.present { background: #b8860b; border-color: #b8860b; }
    .th-now .ts-tile.absent { background: #33455a; border-color: #33455a; }
    .th-now .ts-key { border: 1px solid #2c3b4b; border-radius: 7px; background: #1f2b38; }
    .th-now .ts-key.correct { background: #2ea043; border-color: #2ea043; }
    .th-now .ts-key.present { background: #b8860b; border-color: #b8860b; }
    .th-now .ts-key.absent { background: #0f1720; color: #93a3b5; }
  `,
})
export class NightCourtTheme {}
