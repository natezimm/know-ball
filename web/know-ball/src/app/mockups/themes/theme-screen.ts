import { Component, ViewEncapsulation, ChangeDetectionStrategy } from '@angular/core';

type Mark = 'correct' | 'present' | 'absent' | 'typed' | '';

interface Tile {
  letter: string;
  mark: Mark;
}

const row = (word: string, marks: Mark[]): Tile[] => marks.map((mark, i) => ({ letter: word.charAt(i), mark }));

const KEY_ROWS = [[...'qwertyuiop'], [...'asdfghjkl'], ['Enter', ...'zxcvbnm', '⌫']];
const KEY_MARKS: Record<string, Mark> = {
  b: 'correct', i: 'correct', l: 'present', t: 'present', e: 'absent', a: 'absent', m: 'absent', s: 'absent', u: 'absent',
};

/**
 * The Spordle screen, three guesses into a game whose answer is BLITZ, with layout only.
 * Every colour, font and shape comes from the theme wrapped around it (see ./skins), which
 * styles the `ts-` classes below. The styles are global on purpose so the skins can reach in.
 */
@Component({
  selector: 'app-theme-screen',
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="ts">
      <header class="ts-bar">
        <span class="ts-brand"><i class="ts-mark"></i><b>Know Ball</b></span>
        <nav class="ts-nav"><span class="on">Games</span><span>Search</span></nav>
      </header>
      <div class="ts-main">
        <div class="ts-top">
          <h4 class="ts-title">Spordle</h4>
          <div class="ts-actions"><span class="ts-btn">New game</span><span class="ts-btn">Stats</span></div>
        </div>
        <div class="ts-toolbar">
          <div class="ts-seg"><span class="ts-label">Letters</span><i>4</i><i class="on">5</i><i>6</i></div>
          <span class="ts-status">Guess 4 of 6</span>
        </div>
        <div class="ts-hints">
          <span class="ts-label">Hints</span><span class="ts-chip on">Football</span><span class="ts-chip">Letter</span>
        </div>
        <div class="ts-grid">
          @for (r of rows; track $index) {
            <div class="ts-row">
              @for (t of r; track $index) {
                <span class="ts-tile" [class]="t.mark">{{ t.letter }}</span>
              }
            </div>
          }
        </div>
        <div class="ts-keys">
          @for (r of keyRows; track $index) {
            <div>
              @for (k of r; track k) {
                <span class="ts-key" [class]="keyMarks[k] || ''" [class.wide]="k.length > 1 || k === '⌫'">{{ k }}</span>
              }
            </div>
          }
        </div>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .th-host { flex: 1; min-height: 0; display: block; }
    .ts { height: 100%; display: flex; flex-direction: column; position: relative; overflow: hidden; box-sizing: border-box; }
    .ts-bar {
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
      padding: 10px 14px; position: relative; z-index: 1;
    }
    .ts-brand { display: flex; align-items: center; gap: 7px; font-size: 15px; font-weight: 700; white-space: nowrap; }
    .ts-mark { width: 16px; height: 16px; flex: none; }
    .ts-nav { display: flex; gap: 4px; font-size: 11px; }
    .ts-nav span { padding: 4px 8px; }
    .ts-main {
      flex: 1; min-height: 0; display: flex; flex-direction: column; gap: 10px;
      padding: 12px 14px 14px; position: relative; z-index: 1;
    }
    .ts-top, .ts-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .ts-title { margin: 0; font-size: 26px; line-height: 1; }
    .ts-actions, .ts-seg, .ts-hints { display: flex; align-items: center; gap: 5px; }
    .ts-btn, .ts-chip { padding: 6px 11px; font-size: 12px; line-height: 1.2; white-space: nowrap; }
    .ts-seg i {
      width: 30px; height: 26px; display: grid; place-items: center; box-sizing: border-box;
      font-style: normal; font-size: 13px; font-weight: 700;
    }
    .ts-label { font-size: 10px; letter-spacing: .1em; text-transform: uppercase; margin-right: 3px; }
    .ts-status { font-size: 12px; white-space: nowrap; }
    .ts-grid { display: grid; gap: 5px; justify-content: center; }
    .ts-row { display: grid; grid-template-columns: repeat(5, 46px); gap: 5px; }
    .ts-tile {
      height: 46px; display: grid; place-items: center; position: relative; box-sizing: border-box;
      font-size: 24px; font-weight: 700; line-height: 1; text-transform: uppercase;
    }
    .ts-keys { margin-top: auto; display: grid; gap: 5px; }
    .ts-keys div { display: flex; justify-content: center; gap: 4px; }
    .ts-key {
      flex: 1 1 0; max-width: 29px; height: 40px; display: grid; place-items: center; box-sizing: border-box;
      font-size: 13px; font-weight: 700; text-transform: uppercase;
    }
    .ts-key.wide { flex-grow: 1.6; max-width: 46px; font-size: 9px; }
  `,
})
export class ThemeScreen {
  protected readonly keyRows = KEY_ROWS;
  protected readonly keyMarks = KEY_MARKS;
  protected readonly rows: Tile[][] = [
    row('teams', ['present', 'absent', 'absent', 'absent', 'absent']),
    row('limbs', ['present', 'present', 'absent', 'present', 'absent']),
    row('built', ['correct', 'absent', 'correct', 'present', 'present']),
    row('bli', ['typed', 'typed', 'typed', '', '']),
    row('', ['', '', '', '', '']),
    row('', ['', '', '', '', '']),
  ];
}
