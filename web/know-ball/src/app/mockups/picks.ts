import { Injectable, computed, signal } from '@angular/core';
import { MOCK_GAMES } from './mock-data';

const STORAGE_KEY = 'knowball.mockup-picks';

/** Which design options have been starred, per game. Kept in localStorage so a reload doesn't lose them. */
@Injectable({ providedIn: 'root' })
export class MockupPicks {
  private readonly picks = signal<Record<string, string[]>>(load());

  /** "Scouting Report: B · Career Path: A, C" */
  readonly summary = computed(() =>
    MOCK_GAMES.filter((g) => this.picks()[g.path]?.length)
      .map((g) => `${g.name}: ${this.picks()[g.path].join(', ')}`)
      .join(' · '),
  );

  chosen(game: string): string {
    return (this.picks()[game] ?? []).join(', ');
  }

  has(game: string, letter: string): boolean {
    return this.picks()[game]?.includes(letter) ?? false;
  }

  toggle(game: string, letter: string): void {
    const current = this.picks()[game] ?? [];
    const next = current.includes(letter) ? current.filter((l) => l !== letter) : [...current, letter].sort();
    this.picks.update((p) => ({ ...p, [game]: next }));
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this.picks()));
    } catch {
      // Storage blocked (private window): picks last for this visit only.
    }
  }
}

function load(): Record<string, string[]> {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}');
  } catch {
    return {};
  }
}
