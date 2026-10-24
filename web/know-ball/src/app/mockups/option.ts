import { Component, booleanAttribute, computed, inject, input, ChangeDetectionStrategy } from '@angular/core';
import { MockupPicks } from './picks';

/** One design option: a labelled phone frame plus its trade-offs. The screen content is projected in. */
@Component({
  selector: 'app-mock-option',
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <header class="mk-opt">
      <span class="mk-letter">{{ letter() }}</span>
      <h3>{{ name() }}</h3>
      <button
        type="button"
        class="mk-pick"
        [class.on]="picked()"
        [attr.aria-pressed]="picked()"
        (click)="picks.toggle(game(), letter())"
      >
        {{ picked() ? '★ Picked' : '☆ Pick' }}
      </button>
    </header>
    <div class="mk-phone" role="img" [attr.aria-label]="'Mockup of option ' + letter() + ': ' + name()">
      @if (!bare()) {
        <div class="mk-appbar"><span>‹</span><b>{{ bar() }}</b><span>? ▥</span></div>
      }
      <div class="mk-body" [class.flush]="flush() || bare()"><ng-content /></div>
    </div>
    <ul class="mk-notes">
      <li class="plus">{{ plus() }}</li>
      <li class="minus">{{ minus() }}</li>
    </ul>
  `,
})
export class MockOption {
  readonly game = input.required<string>();
  readonly letter = input.required<string>();
  readonly name = input.required<string>();
  /** Title shown in the phone's app bar. */
  readonly bar = input('');
  readonly plus = input.required<string>();
  readonly minus = input.required<string>();
  /** Drop the screen padding, for edge-to-edge layouts such as a map. */
  readonly flush = input(false, { transform: booleanAttribute });
  /** Leave the whole screen to the content: no app bar and no padding. For theme previews. */
  readonly bare = input(false, { transform: booleanAttribute });

  protected readonly picks = inject(MockupPicks);
  protected readonly picked = computed(() => this.picks.has(this.game(), this.letter()));
}
