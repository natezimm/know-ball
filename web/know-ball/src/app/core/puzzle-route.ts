import { effect, untracked } from '@angular/core';
import { GameSlug, League } from './game-api';
import { GameStore } from './game-store';

/**
 * Keeps a game page in step with its address: "/career-path" is today's puzzle and
 * "/career-path/puzzle/41" is a past or practice one. "/career-path/nfl" is today's NFL puzzle; the route's
 * data names the league. Call from the page's constructor.
 */
export function loadFromRoute(store: GameStore, game: GameSlug, puzzleId: () => string | undefined, league: () => League | undefined = () => undefined): void {
  effect(() => {
    const raw = puzzleId();
    const edition = league() ?? 'nba';
    const id = raw === undefined ? NaN : Number(raw);
    // Only the address should re-run this; loading reads and writes the store's own state.
    untracked(() => store.load(game, Number.isInteger(id) ? id : undefined, edition));
  });
}
