import { Injectable, computed, signal } from '@angular/core';
import { MOCK_GAMES } from './mock-data';

const STORAGE_KEY = 'spordle.mockup-picks.v2';
// v1 keyed the player-guessing game as "spordle"; that path now belongs to the word game.
const LEGACY_KEY = 'spordle.mockup-picks';
const RENAMED: Record<string, string> = { spordle: 'scouting-report', 'word-game': 'spordle' };

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
    const current = localStorage.getItem(STORAGE_KEY);
    if (current) return JSON.parse(current);
    const legacy: Record<string, string[]> = JSON.parse(localStorage.getItem(LEGACY_KEY) ?? '{}');
    return Object.fromEntries(Object.entries(legacy).map(([game, letters]) => [RENAMED[game] ?? game, letters]));
  } catch {
    return {};
  }
}
