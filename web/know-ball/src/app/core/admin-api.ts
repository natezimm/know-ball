import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { WhosThatAnswer, WhosThatClues } from '../games/whos-that/whos-that-stage';
import { Observable } from 'rxjs';

const KEY_STORAGE = 'knowball.adminKey';

export interface AdminOverview {
  today: string;
  games: { game: string; name: string; generated: boolean; daysQueued: number; queuedThrough: string | null }[];
  data: { players: number; activePlayers: number; playersWithHeadshots: number; teams: number };
  imports: { source: string; status: string; finishedAt: string | null; error: string | null }[];
}

export interface CuratedPuzzle {
  id: number;
  state: 'draft' | 'scheduled' | 'published';
  date: string | null;
  number: number | null;
  playerId: number;
  playerName: string;
  year: number;
  team: string;
  caption: string | null;
  silhouetteUrl: string;
  blurredUrl: string;
  photoUrl: string;
  attribution?: { sourceUrl: string | null; credit: string | null; license: string | null } | null;
}

export interface CuratedPreview { clues: WhosThatClues | null; answer: WhosThatAnswer | null; }

/**
 * The admin area's API. Every call carries the admin key, which is kept for this browser tab
 * only and is gone when the tab closes.
 */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);
  private readonly puzzles = '/api/admin/whos-that/puzzles';

  readonly key = signal(readKey());

  setKey(key: string): void {
    this.key.set(key);
    try {
      if (key) sessionStorage.setItem(KEY_STORAGE, key);
      else sessionStorage.removeItem(KEY_STORAGE);
    } catch {
      // Storage blocked: the key lasts until the page is reloaded.
    }
  }

  /** Also the way to check a key: it answers 401 for a wrong one and 404 when the area is switched off. */
  overview(key = this.key()): Observable<AdminOverview> {
    return this.http.get<AdminOverview>('/api/admin/overview', { headers: this.headers(key) });
  }

  list(): Observable<CuratedPuzzle[]> {
    return this.http.get<CuratedPuzzle[]>(this.puzzles, { headers: this.headers() });
  }

  create(form: FormData): Observable<CuratedPuzzle> {
    return this.http.post<CuratedPuzzle>(this.puzzles, form, { headers: this.headers() });
  }

  update(id: number, form: FormData): Observable<CuratedPuzzle> {
    return this.http.put<CuratedPuzzle>(`${this.puzzles}/${id}`, form, { headers: this.headers() });
  }

  preview(id: number, misses: number, reveal: boolean): Observable<CuratedPreview> {
    return this.http.get<CuratedPreview>(`${this.puzzles}/${id}/preview`, {
      headers: this.headers(), params: { misses, reveal },
    });
  }

  /** With no date the puzzle takes the first free day. */
  schedule(id: number, date: string | null): Observable<CuratedPuzzle> {
    return this.http.post<CuratedPuzzle>(`${this.puzzles}/${id}/schedule`, date ? { date } : {}, { headers: this.headers() });
  }

  unschedule(id: number): Observable<CuratedPuzzle> {
    return this.http.post<CuratedPuzzle>(`${this.puzzles}/${id}/unschedule`, null, { headers: this.headers() });
  }

  move(id: number, direction: 'earlier' | 'later'): Observable<CuratedPuzzle> {
    return this.http.post<CuratedPuzzle>(`${this.puzzles}/${id}/move`, { direction }, { headers: this.headers() });
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`${this.puzzles}/${id}`, { headers: this.headers() });
  }

  private headers(key = this.key()): HttpHeaders {
    return new HttpHeaders({ 'X-Admin-Key': key });
  }
}

function readKey(): string {
  try {
    return sessionStorage.getItem(KEY_STORAGE) ?? '';
  } catch {
    return '';
  }
}
