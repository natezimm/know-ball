import { GameSlug, GameState, GuessRecord } from './game-api';

/** A puzzle as the API sends it, for specs. Override whatever the test is about. */
export function gameState(overrides: Partial<GameState> = {}): GameState {
  return {
    game: 'scouting-report' as GameSlug,
    puzzleId: 7,
    number: 12,
    date: '2026-10-13',
    practice: false,
    maxGuesses: 8,
    status: 'playing',
    payload: {},
    clues: null,
    guesses: [],
    answer: null,
    nextPuzzleAt: null,
    ...overrides,
  };
}

export function guess(name: string, correct = false, feedback: unknown = null, playerId = name.length): GuessRecord {
  return { playerId, name, headshotUrl: null, correct, feedback };
}
