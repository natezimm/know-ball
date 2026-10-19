import { Component, computed, input, signal } from '@angular/core';

export interface WhosThatClues {
  silhouetteUrl: string;
  /** Each is null until enough wrong guesses have released it. */
  year: number | null;
  team: string | null;
  blurredUrl: string | null;
}

export interface WhosThatAnswer {
  playerId: number;
  name: string;
  headshotUrl: string | null;
  photoUrl: string;
  caption: string | null;
  year: number;
  team: string;
  attribution?: { sourceUrl: string | null; credit: string | null; license: string | null } | null;
}


/** Shared by the game and the admin preview, so both show the same photo and attribution. */
@Component({
  selector: 'app-whos-that-stage',
  template: `
        <figure class="stage">
          <div class="frame" [class.photo]="reveal() || view() === 'blurred'">
            @if (reveal(); as a) {
              <img [src]="a.photoUrl" [alt]="'The photo: ' + a.name" />
            } @else if (view() === 'blurred') {
              <img [src]="clues()!.blurredUrl" alt="The photo, heavily blurred" />
            } @else {
              <img [src]="clues()?.silhouetteUrl" alt="Black outline of the mystery player" />
            }
          </div>
          @if (reveal()?.caption || reveal()?.attribution) {
            <figcaption>
              @if (reveal()?.caption; as caption) { <span>{{ caption }}</span> }
              @if (reveal()?.attribution; as credit) {
              <span class="credit">
              @if (credit.credit) { <span>Photo: {{ credit.credit }}</span> }
              @if (credit.license) { <span>{{ credit.license }}</span> }
              @if (safeSource(); as source) { <a [href]="source" target="_blank" rel="noopener noreferrer">Photo source</a> }
              </span>
              }
            </figcaption>
          }
        </figure>

        @if (!reveal() && clues()?.blurredUrl) {
          <div class="views" role="group" aria-label="Which picture to show">
            <button type="button" class="kb-btn" [attr.aria-pressed]="view() === 'outline'" (click)="picked.set('outline')">Outline</button>
            <button type="button" class="kb-btn" [attr.aria-pressed]="view() === 'blurred'" (click)="picked.set('blurred')">Blurred photo</button>
          </div>
        }

  `,
  styles: `
    :host { display: block; }
    /* A spotlit stage in a card frame. The outline stands on it; photos fill it, with the caption underneath. */
    .stage {
      margin: 0; overflow: hidden; background: var(--navy);
      border: 2px solid var(--navy); border-radius: 6px; box-shadow: 0 0 0 2px var(--cream), 0 0 0 6px var(--navy);
    }
    .frame {
      aspect-ratio: 4 / 3; position: relative;
      background: radial-gradient(circle at 50% 38%, #fbe9b4, var(--mustard) 52%, #9a6a12);
    }
    /* Images stay out of layout so tall portraits cannot enlarge the aspect-ratio frame. */
    .frame img { position: absolute; left: 4%; bottom: 0; width: 92%; height: 94%; display: block; object-fit: contain; object-position: center bottom; }
    .frame.photo { background: var(--navy); }
    .frame.photo img { inset: 0; width: 100%; height: 100%; object-position: center; }
    figcaption { padding: 8px 12px 9px; color: var(--cream); font-size: 16px; font-style: italic; line-height: 1.25; }

    .views { display: flex; gap: 6px; margin-top: 4px; }
    .views [aria-pressed="true"] { background: var(--navy); color: var(--cream); }


    .credit { display: flex; flex-wrap: wrap; gap: 6px 12px; font-style: normal; overflow-wrap: anywhere; }
    .credit > span { min-width: 0; max-width: 100%; }
    .credit a { color: var(--cream); text-decoration: underline; overflow-wrap: anywhere; }
  `,
})
export class WhosThatStage {
  readonly clues = input<WhosThatClues | null>(null);
  readonly reveal = input<WhosThatAnswer | null>(null);
  protected readonly picked = signal<'outline' | 'blurred' | null>(null);
  protected readonly view = computed(() => this.clues()?.blurredUrl ? (this.picked() ?? 'blurred') : 'outline');
  protected readonly safeSource = computed(() => {
    const source = this.reveal()?.attribution?.sourceUrl;
    if (!source) return null;
    try {
      const url = new URL(source);
      return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? source : null;
    } catch { return null; }
  });
}
