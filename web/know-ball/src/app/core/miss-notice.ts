import { effect, untracked } from '@angular/core';
import { GameState, GuessRecord, isMiss } from './game-api';
import { GameStore } from './game-store';

/**
 * Says what a miss did, in the notice beside the guess box. A wrong guess is listed under the puzzle and what it
 * uncovers may be further down still, both off the screen of a phone while the player is typing.
 * `opened` names what this miss uncovered, or returns null when it uncovered nothing. Call from the page's constructor.
 */
export function noticeOnMiss<TState extends GameState<any, any, any, any>>(
  store: GameStore,
  state: () => TState | null,
  opened: (now: TState, before: TState) => string | null,
): void {
  let before: TState | null = null;
  effect(() => {
    const now = state();
    // A reload passes through null; the puzzle that comes back is compared with the one that was there.
    if (!now) return;
    const previous = before;
    before = now;
    if (!previous || previous.puzzleId !== now.puzzleId || now.guesses.length !== previous.guesses.length + 1) return;

    const missed = now.guesses[now.guesses.length - 1] as GuessRecord;
    // The last guess of a lost game needs no notice: the answer card says it all.
    if (now.status !== 'playing' || !isMiss(missed)) return;
    const what = opened(now, previous);
    untracked(() => store.notice.set(what ? `Not ${missed.name}. ${what}` : `Not ${missed.name}.`));
  });
}
