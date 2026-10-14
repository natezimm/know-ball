import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type GameSlug = 'scouting-report' | 'career-path' | 'whos-that';
export type PlayStatus = 'playing' | 'won' | 'lost';
/** As shown in lists: a puzzle not yet started is "new". */
export type ResultStatus = PlayStatus | 'new';

export interface GuessRecord<TFeedback = unknown> {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  correct: boolean;
  feedback: TFeedback | null;
}

/** A puzzle with this player's progress. `answer` stays null until the game is over. */
export interface GameState<TPayload = unknown, TClues = unknown, TFeedback = unknown, TAnswer = PuzzleAnswer> {
  game: GameSlug;
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

  daily(game: GameSlug): Observable<GameState> {
    return this.http.get<GameState>(`/api/${game}/daily`);
  }

  puzzle(game: GameSlug, puzzleId: number): Observable<GameState> {
    return this.http.get<GameState>(`/api/${game}/puzzles/${puzzleId}`);
  }

  guess(game: GameSlug, puzzleId: number, playerId: number): Observable<GameState> {
    return this.http.post<GameState>(`/api/${game}/${puzzleId}/guess`, { playerId });
  }

  practice(game: GameSlug): Observable<GameState> {
    return this.http.post<GameState>(`/api/${game}/practice`, null);
  }

  archive(game: GameSlug): Observable<ArchiveEntry[]> {
    return this.http.get<ArchiveEntry[]>(`/api/${game}/archive`);
  }

  stats(): Observable<GameStats[]> {
    return this.http.get<GameStats[]>('/api/me/stats');
  }

  today(): Observable<TodayEntry[]> {
    return this.http.get<TodayEntry[]>('/api/me/today');
  }
}
