import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface PlayerSearchResult {
  id: number;
  fullName: string;
  position: string | null;
  teamAbbreviation: string | null;
  teamName: string | null;
  headshotUrl: string | null;
  isActive?: boolean;
  /** Career span, "1996–2016"; open-ended for current players. */
  years?: string | null;
}

export const MIN_QUERY_LENGTH = 2;

@Injectable({ providedIn: 'root' })
export class PlayerSearchService {
  private readonly http = inject(HttpClient);

  /** `includeInactive` adds retired players, for the games whose answers are not all current. */
  search(query: string, league = 'nba', limit = 8, includeInactive = false): Observable<PlayerSearchResult[]> {
    let params = new HttpParams().set('q', query).set('league', league).set('limit', limit);
    if (includeInactive) params = params.set('includeInactive', true);
    return this.http.get<PlayerSearchResult[]>('/api/players/search', { params });
  }
}
