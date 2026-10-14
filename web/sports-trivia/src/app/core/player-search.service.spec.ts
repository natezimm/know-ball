import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PlayerSearchService } from './player-search.service';

describe('PlayerSearchService', () => {
  let service: PlayerSearchService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(PlayerSearchService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('calls the search endpoint with query, league and limit', () => {
    let names: string[] = [];
    service.search('jok', 'nba', 5).subscribe((rows) => (names = rows.map((r) => r.fullName)));

    const req = http.expectOne((r) => r.url === '/api/players/search');
    expect(req.request.params.get('q')).toBe('jok');
    expect(req.request.params.get('league')).toBe('nba');
    expect(req.request.params.get('limit')).toBe('5');
    req.flush([{ id: 1, fullName: 'Nikola Jokić', position: 'C', teamAbbreviation: 'DEN', teamName: 'Denver Nuggets', headshotUrl: null }]);

    expect(names).toEqual(['Nikola Jokić']);
  });
});
