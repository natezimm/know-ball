import { HttpClient, provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { randomHex, TRACE_HEADER, traceContextInterceptor } from './trace-context';

describe('trace context', () => {
  function setUp() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr(), withInterceptors([traceContextInterceptor])), provideHttpClientTesting()],
    });
    return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
  }

  it('starts a fresh unsampled W3C trace for each call to our API', () => {
    const { http, backend } = setUp();

    http.get('/api/games/today').subscribe();
    http.get('/api/games/today').subscribe();
    const [first, second] = backend.match('/api/games/today').map(r => r.request.headers.get(TRACE_HEADER));

    expect(first).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-00$/);
    expect(second).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-00$/);
    expect(second!.slice(3, 35)).not.toBe(first!.slice(3, 35));
  });

  it('leaves calls to other sites alone', () => {
    const { http, backend } = setUp();

    http.get('https://example.com/logo.png').subscribe();

    expect(backend.expectOne('https://example.com/logo.png').request.headers.has(TRACE_HEADER)).toBe(false);
  });

  it('logs the trace id when the server fails', () => {
    const { http, backend } = setUp();
    const errors: string[] = [];
    const original = console.error;
    console.error = (message: string) => errors.push(message);
    try {
      http.get('/api/games/today').subscribe({ error: () => {} });
      const request = backend.expectOne('/api/games/today');
      const traceId = request.request.headers.get(TRACE_HEADER)!.slice(3, 35);
      request.flush('down', { status: 503, statusText: 'Unavailable' });

      expect(errors).toEqual([`Know Ball API call failed (503); trace id ${traceId}`]);
    } finally {
      console.error = original;
    }
  });

  it('never makes an all-zero id', () => {
    expect(randomHex(8)).toMatch(/^[0-9a-f]{16}$/);
  });
});
