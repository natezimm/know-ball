import { APIRequestContext, Page, expect, test as base } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

export type Game = 'scouting-report' | 'career-path' | 'whos-that';

export interface Player {
  id: number;
  fullName: string;
}

interface Guess {
  playerId: number;
  correct: boolean;
}

interface State {
  puzzleId: number;
  maxGuesses: number;
  status: 'playing' | 'won' | 'lost';
  guesses: Guess[];
  answer: { playerId: number; name: string } | null;
}

export const repoRoot = resolve(__dirname, '../../..');

/** Every page starts as a new visitor. Requests that leave the site (team logos, headshots) are cut off, so nothing depends on the network. */
export const test = base.extend({
  page: async ({ page, baseURL }, use) => {
    await page.route(
      (url) => url.origin !== new URL(baseURL!).origin,
      (route) => route.abort(),
    );
    await use(page);
  },
});

export { expect };

/** Asks the API as a new player of its own, so the browser under test keeps a clean slate. */
export async function asPlayer(request: APIRequestContext, anon = `e2e-${randomUUID()}`) {
  const headers = { 'X-Anon-Id': anon };
  return {
    daily: async (game: Game): Promise<State> => ok(await request.get(`/api/${game}/daily`, { headers })),
    guess: async (game: Game, puzzleId: number, playerId: number): Promise<State> =>
      ok(await request.post(`/api/${game}/${puzzleId}/guess`, { headers, data: { playerId } })),
  };
}

/** The bundled seed players, as the guess box finds them. */
export async function players(request: APIRequestContext, includeInactive: boolean): Promise<Player[]> {
  const seed = JSON.parse(readFileSync(resolve(repoRoot, 'src/KnowBall.Infrastructure/Data/nba-sample-players.json'), 'utf8'));
  const found = new Map<number, Player>();
  for (const p of seed.players as { firstName: string; lastName: string }[]) {
    const q = `${p.firstName} ${p.lastName}`;
    const rows: Player[] = await ok(await request.get('/api/players/search', { params: { q, includeInactive, limit: 3 } }));
    const match = rows.find((r) => r.fullName === q);
    if (match) found.set(match.id, { id: match.id, fullName: match.fullName });
  }
  return [...found.values()];
}

/**
 * Today's answer and enough players known to be wrong to lose with, found by playing the daily
 * through the API as throwaway players. The game itself then decides what is right or wrong, so
 * a test never depends on which puzzle the seed data produced.
 */
export async function learnDaily(request: APIRequestContext, game: Game, pool: Player[]) {
  let answer: Player | undefined;
  const wrong: Player[] = [];
  const tried = new Set<number>();

  for (let round = 0; round < 10; round++) {
    const api = await asPlayer(request);
    let state = await api.daily(game);
    if (answer && wrong.length >= state.maxGuesses) return { answer, wrong, maxGuesses: state.maxGuesses };

    for (const player of pool) {
      if (tried.has(player.id)) continue;
      tried.add(player.id);
      state = await api.guess(game, state.puzzleId, player.id);
      const last = state.guesses[state.guesses.length - 1];
      if (!last.correct) wrong.push(player);
      if (state.answer) answer = pool.find((p) => p.id === state.answer!.playerId) ?? { id: state.answer.playerId, fullName: state.answer.name };
      if (state.status !== 'playing') break;
    }
    if (answer && wrong.length >= state.maxGuesses) return { answer, wrong, maxGuesses: state.maxGuesses };
    if (tried.size >= pool.length) break;
  }
  throw new Error(`Could not learn today's ${game} answer from ${pool.length} players`);
}

/** Types a name into the guess box, picks it from the list and waits for the server's verdict. */
export async function guessPlayer(page: Page, name: string): Promise<void> {
  const box = page.getByRole('combobox', { name: 'Guess a player' });
  await expect(box).toBeEnabled();
  await box.fill(name);
  const option = page.getByRole('option').filter({ has: page.getByText(name, { exact: true }) });
  const verdict = page.waitForResponse((r) => r.request().method() === 'POST' && /\/guess$/.test(r.url()));
  await option.first().click();
  expect((await verdict).ok()).toBe(true);
}

async function ok(response: { ok(): boolean; status(): number; url(): string; json(): Promise<any> }) {
  if (!response.ok()) throw new Error(`${response.status()} from ${response.url()}`);
  return response.json();
}
