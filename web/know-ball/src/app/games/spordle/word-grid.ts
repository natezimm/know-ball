import { Component, computed, input } from '@angular/core';
import { Attempt, LetterHint, MAX_ATTEMPTS } from './spordle-game';

interface Tile {
  letter: string;
  cls: string;
}

interface Row {
  label: string;
  active: boolean;
  tiles: Tile[];
}

/** The six rows of tiles. Scored rows flip one tile at a time to show their colours. */
@Component({
  selector: 'app-spordle-grid',
  template: `
    <div class="grid" [style.--cols]="wordLength()">
      @for (row of rows(); track $index) {
        <div class="row" role="group" [class.active]="row.active" [attr.aria-label]="row.label">
          @for (tile of row.tiles; track $index) {
            <span class="tile" aria-hidden="true" [class]="tile.cls" [style.--i]="$index">{{ tile.letter }}</span>
          }
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .grid { --gap: 6px; display: grid; gap: var(--gap); justify-content: center; }
    .row { display: grid; grid-template-columns: repeat(var(--cols), var(--tile, 52px)); gap: var(--gap); }
    .tile {
      height: var(--tile, 52px); border-radius: 4px; border: 2px solid var(--line); background: var(--surface);
      display: grid; place-items: center; user-select: none; text-transform: uppercase;
      font-family: var(--display); font-weight: 400; font-size: calc(var(--tile, 52px) * .5); line-height: 1;
    }
    .tile { border-color: rgb(29 43 83 / .4); }
    .row.active .tile { border-color: var(--line); }
    .row.active .tile.filled { border-color: var(--accent); animation: pop 120ms ease-out; }
    .row.active .tile.cursor { box-shadow: inset 0 -5px 0 var(--mustard); }
    .row.active .tile.ghost { color: var(--muted); border-style: dashed; }

    .tile.correct { --status: var(--ok); --status-ink: var(--ok-ink); }
    .tile.present { --status: var(--close); --status-ink: var(--close-ink); }
    .tile.absent { --status: var(--miss); --status-ink: var(--miss-ink); }
    /* The static colours are the end state; the animation hides them until each tile's turn. */
    .tile.scored {
      background: var(--status); border-color: var(--line); color: var(--status-ink);
      animation: flip 500ms ease both; animation-delay: calc(var(--i) * 300ms);
    }

    @keyframes pop {
      50% { transform: scale(1.08); }
    }
    /* The colour changes while the tile is edge-on, so the swap itself is never seen. */
    @keyframes flip {
      0% { transform: rotateX(0); background: var(--surface); border-color: var(--line); color: var(--text); }
      49.9% { transform: rotateX(90deg); background: var(--surface); border-color: var(--line); color: var(--text); }
      50% { transform: rotateX(90deg); background: var(--status); border-color: var(--line); color: var(--status-ink); }
      100% { transform: rotateX(0); }
    }
    @media (prefers-reduced-motion: reduce) {
      .tile.filled, .tile.scored { animation: none; }
    }
  `,
})
export class SpordleGrid {
  readonly attempts = input.required<Attempt[]>();
  readonly currentGuess = input.required<string>();
  readonly wordLength = input.required<number>();
  /** Whether the player can type into the next row. */
  readonly active = input(true);
  /** Shown faintly in its position on the row being typed, until a letter is typed over it. */
  readonly hint = input<LetterHint | null>(null);

  protected readonly rows = computed<Row[]>(() => {
    const attempts = this.attempts();
    const guess = this.currentGuess();
    const length = this.wordLength();
    const hint = this.hint();
    const positions = Array.from({ length }, (_, i) => i);

    return Array.from({ length: MAX_ATTEMPTS }, (_, r): Row => {
      const attempt = attempts[r];
      if (attempt) {
        return {
          active: false,
          label: `Guess ${r + 1}: ` + positions.map((i) => `${attempt.word[i]} ${attempt.score[i]}`).join(', '),
          tiles: positions.map((i) => ({ letter: attempt.word[i], cls: `scored ${attempt.score[i]}` })),
        };
      }

      if (r === attempts.length && this.active()) {
        return {
          active: true,
          label: `Guess ${r + 1}: ${guess || 'empty'}`,
          tiles: positions.map((i) => {
            if (guess[i]) return { letter: guess[i], cls: 'filled' };
            const cursor = i === guess.length ? 'cursor' : '';
            return hint?.position === i + 1 ? { letter: hint.letter, cls: `ghost ${cursor}` } : { letter: '', cls: cursor };
          }),
        };
      }

      return { active: false, label: `Guess ${r + 1}: not used`, tiles: positions.map(() => ({ letter: '', cls: '' })) };
    });
  });
}
