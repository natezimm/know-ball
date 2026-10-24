import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { useMemoryStorage } from '../games/spordle/memory-storage';
import { ANON_HEADER, anonId, anonIdInterceptor } from './anon-id';

describe('anonymous player id', () => {
  beforeEach(() => useMemoryStorage());

  it('is made once and then kept', () => {
    const first = anonId();

    expect(first).toMatch(/^[A-Za-z0-9-]{8,64}$/);
    expect(anonId()).toBe(first);
    expect(localStorage.getItem('knowball.anonId')).toBe(first);
  });

  it('replaces a stored value the server would refuse', () => {
    localStorage.setItem('knowball.anonId', 'no good!');

    expect(anonId()).toMatch(/^[A-Za-z0-9-]{8,64}$/);
  });

  it('still works when storage is blocked', () => {
    Object.defineProperty(globalThis, 'localStorage', {
      configurable: true,
      get: () => {
        throw new Error('blocked');
      },
    });

    const id = anonId();
    expect(id).toMatch(/^[A-Za-z0-9-]{8,64}$/);
    expect(anonId()).toBe(id);
  });

  it('goes out with calls to our API and nowhere else', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([anonIdInterceptor])), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);

    http.get('/api/me/today').subscribe();
    http.get('https://cdn.example/logo.svg').subscribe();

    expect(backend.expectOne('/api/me/today').request.headers.get(ANON_HEADER)).toBe(anonId());
    expect(backend.expectOne('https://cdn.example/logo.svg').request.headers.has(ANON_HEADER)).toBe(false);
  });
});
