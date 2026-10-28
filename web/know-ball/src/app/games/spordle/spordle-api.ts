import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LetterStatus } from './scoring';

export interface StartedGame {
  gameId: string;
  wordLength: number;
  maxAttempts: number;
}

export interface SavedGame extends StartedGame {
  guesses: { requestId: string; word: string; score: LetterStatus[] }[];
  complete: boolean;
  won: boolean;
  answer: string | null;
  letterHint: { position: number; letter: string } | null;
}

/** A word outside the dictionary comes back as just `{ valid: false }`. `answer` is only sent once the game is over. */
export interface GuessResult {
  valid: boolean;
  score?: LetterStatus[];
  won?: boolean;
  complete?: boolean;
  attemptsRemaining?: number;
  answer?: string;
}

export type HintType = 'category' | 'letter';

/** A category hint fills `category`; a letter hint fills `position` (1-based) and `letter`. */
export interface HintResult {
  category?: string;
  position?: number;
  letter?: string;
}

@Injectable({ providedIn: 'root' })
export class SpordleApi {
  private readonly http = inject(HttpClient);

  startGame(wordLength: number): Observable<StartedGame> {
    return this.http.post<StartedGame>('/api/spordle/games', { wordLength });
  }

  getGame(gameId: string): Observable<SavedGame> {
    return this.http.get<SavedGame>(`/api/spordle/games/${encodeURIComponent(gameId)}`);
  }

  submitGuess(gameId: string, word: string, requestId: string): Observable<GuessResult> {
    return this.http.post<GuessResult>(`/api/spordle/games/${encodeURIComponent(gameId)}/guesses`, { word, requestId });
  }

  requestHint(gameId: string, type: HintType): Observable<HintResult> {
    return this.http.post<HintResult>(`/api/spordle/games/${encodeURIComponent(gameId)}/hint`, { type });
  }
}
