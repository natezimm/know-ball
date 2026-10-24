import { Component, input, output, ChangeDetectionStrategy } from '@angular/core';
import { LetterStatus } from './scoring';

const ROWS = [[...'qwertyuiop'], [...'asdfghjkl'], ['Enter', ...'zxcvbnm', 'Backspace']];

/** On-screen keyboard. Keys take the colour of the best result seen for that letter. */
@Component({
  selector: 'app-spordle-keyboard',
  template: `
    <div class="keyboard" role="group" aria-label="Keyboard">
      @for (row of rows; track $index) {
        <div class="keys">
          @for (key of row; track key) {
            <!-- Keeping focus off a clicked key stops a physical Enter from "clicking" it again. -->
            <button
              type="button"
              class="key"
              [class]="statuses()[key] || ''"
              [class.wide]="key.length > 1"
              [attr.aria-label]="label(key)"
              (mousedown)="$event.preventDefault()"
              (click)="pressed.emit(key)"
            >
              {{ key === 'Backspace' ? '⌫' : key }}
            </button>
          }
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    :host { display: block; }
    .keyboard { display: grid; gap: 6px; }
    .keys { display: flex; justify-content: center; gap: 5px; }
    .key {
      flex: 1 1 0; min-width: 0; max-width: 44px; height: clamp(42px, 6.4dvh, 52px); padding: 0;
      border: 2px solid var(--line); border-radius: 4px; background: var(--surface); color: var(--text);
      font: 700 18px/1 var(--font); text-transform: uppercase; cursor: pointer; touch-action: manipulation;
    }
    .key:hover { filter: brightness(.96); }
    .key:active { transform: translateY(1px); }
    .key.wide { flex-grow: 1.6; max-width: 70px; font-size: 12px; letter-spacing: .04em; }
    .key.correct { background: var(--ok); color: var(--ok-ink); }
    .key.present { background: var(--close); color: var(--close-ink); }
    .key.absent { background: var(--miss); border-color: var(--miss-line); color: var(--miss-ink); }
  `,
})
export class SpordleKeyboard {
  readonly statuses = input<Readonly<Record<string, LetterStatus>>>({});
  /** A letter, "Enter" or "Backspace". */
  readonly pressed = output<string>();

  protected readonly rows = ROWS;

  protected label(key: string): string {
    const status = this.statuses()[key];
    return status ? `${key}, ${status}` : key;
  }
}
