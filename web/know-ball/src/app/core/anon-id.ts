import { HttpInterceptorFn } from '@angular/common/http';

const STORAGE_KEY = 'knowball.anonId';
export const ANON_HEADER = 'X-Anon-Id';

let memoryId: string | null = null;

/**
 * The id this browser plays under. Made up on the first visit and kept, so progress and stats
 * follow the browser without an account. If storage is blocked it lasts for this visit only.
 */
export function anonId(): string {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored && /^[A-Za-z0-9-]{8,64}$/.test(stored)) return stored;
    const fresh = newId();
    localStorage.setItem(STORAGE_KEY, fresh);
    return fresh;
  } catch {
    return (memoryId ??= newId());
  }
}

function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  // Older browsers on plain http: random enough to tell players apart.
  return Array.from({ length: 4 }, () => Math.random().toString(36).slice(2, 10).padEnd(8, '0')).join('-');
}

/** Adds the player's id to every call to our own API. */
export const anonIdInterceptor: HttpInterceptorFn = (request, next) =>
  request.url.startsWith('/api/') ? next(request.clone({ setHeaders: { [ANON_HEADER]: anonId() } })) : next(request);
