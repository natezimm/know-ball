import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type GameSlug = 'scouting-report' | 'career-path' | 'back-of-the-card' | 'team-photo';
/** The leagues a game can have an edition in. NBA is every game's first; Scouting Report, Career Path and Back of the Card also have an NFL one. */
export type League = 'nba' | 'nfl';
export const LEAGUE_NAMES: Record<League, string> = { nba: 'NBA', nfl: 'NFL' };
export type PlayStatus = 'playing' | 'won' | 'lost';
/** As shown in lists: a puzzle not yet started is "new". */
export type ResultStatus = PlayStatus | 'new';

export interface GuessRecord<TFeedback = unknown> {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  correct: boolean;
  feedback: TFeedback | null;
  /** Only sent as false, for a wrong guess that does not count as a miss (a bench player in Team Photo). */
  miss?: boolean;
}

/** Whether a guess used up one of the misses: a wrong guess, unless the game says otherwise. */
export function isMiss(guess: GuessRecord): boolean {
  return guess.miss ?? !guess.correct;
}

/** A puzzle with this player's progress. `answer` stays null until the game is over. */
export interface GameState<TPayload = unknown, TClues = unknown, TFeedback = unknown, TAnswer = PuzzleAnswer> {
  game: GameSlug;
  league: League;
  puzzleId: number;
  number: number | null;
  /** ISO date; null for practice rounds. */
  date: string | null;
  practice: boolean;
  maxGuesses: number;
  status: PlayStatus;
  payload: TPayload;
  clues: TClues | null;
  guesses: GuessRecord<TFeedback>[];
  answer: TAnswer | null;
  /** When tomorrow's puzzle goes live; only sent with today's puzzle. */
  nextPuzzleAt: string | null;
}

export interface PuzzleAnswer {
  playerId: number;
  name: string;
  headshotUrl?: string | null;
}

export interface ArchiveEntry {
  puzzleId: number;
  number: number | null;
  date: string;
  status: ResultStatus;
  guesses: number;
}

export interface GameStats {
  game: GameSlug;
  league: League;
  name: string;
  maxGuesses: number;
  played: number;
  wins: number;
  currentStreak: number;
  bestStreak: number;
  /** Wins by number of guesses; index 0 is a first-guess win. */
  distribution: number[];
}

export interface TodayEntry {
  game: GameSlug;
  league: League;
  name: string;
  available: boolean;
  number: number | null;
  status: ResultStatus;
  guesses: number;
  maxGuesses: number;
}

@Injectable({ providedIn: 'root' })
export class GameApi {
  private readonly http = inject(HttpClient);

  daily(game: GameSlug, league: League = 'nba'): Observable<GameState> {
    return this.http.get<GameState>(`/api/${game}/daily${leagueQuery(league)}`);
  }

  puzzle(game: GameSlug, puzzleId: number): Observable<GameState> {
    return this.http.get<GameState>(`/api/${game}/puzzles/${puzzleId}`);
  }

  guess(game: GameSlug, puzzleId: number, playerId: number, requestId?: string): Observable<GameState> {
    return this.http.post<GameState>(`/api/${game}/${puzzleId}/guess`, { playerId, requestId });
  }

  practice(game: GameSlug, league: League = 'nba'): Observable<GameState> {
    return this.http.post<GameState>(`/api/${game}/practice${leagueQuery(league)}`, null);
  }

  archive(game: GameSlug, league: League = 'nba'): Observable<ArchiveEntry[]> {
    return this.http.get<ArchiveEntry[]>(`/api/${game}/archive${leagueQuery(league)}`);
  }

  stats(): Observable<GameStats[]> {
    return this.http.get<GameStats[]>('/api/me/stats');
  }

  today(): Observable<TodayEntry[]> {
    return this.http.get<TodayEntry[]>('/api/me/today');
  }

  /** The slugs of the games switched off for now; usually none. */
  paused(): Observable<string[]> {
    return this.http.get<string[]>('/api/games/paused');
  }
}

/** NBA is the default edition, so its addresses carry no league. */
function leagueQuery(league: League): string {
  return league === 'nba' ? '' : `?league=${league}`;
}

/** The address of a game's daily in a league: "/career-path" or "/career-path/nfl". */
export function dailyLink(game: GameSlug, league: League | undefined): string[] {
  return league && league !== 'nba' ? ['/', game, league] : ['/', game];
}
