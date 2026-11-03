import { Component, computed, input, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { GameRoute, nextGame } from '../../core/games';

/** The way on from a finished game to the next one, so a visitor plays through the set. */
@Component({
  selector: 'app-next-game',
  imports: [RouterLink],
  template: `<a class="kb-btn" [routerLink]="['/', next().slug]">Play {{ next().name }} <span aria-hidden="true">→</span></a>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NextGame {
  readonly current = input.required<GameRoute>();
  protected readonly next = computed(() => nextGame(this.current()));
}
