import { Component, computed, input, signal, ChangeDetectionStrategy } from '@angular/core';

/** A player's headshot in a ring, or their initials when there is no photo or it fails to load. */
@Component({
  selector: 'app-avatar',
  template: `
    @if (src() && !failed()) {
      <img [src]="src()" alt="" (error)="failedSrc.set(src() ?? null)" />
    } @else {
      <span aria-hidden="true">{{ initials() }}</span>
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    :host {
      flex: none; display: grid; place-items: center; overflow: hidden;
      width: var(--size, 44px); height: var(--size, 44px); border-radius: 50%;
      border: 2px solid var(--navy); background: var(--surface-2); color: var(--navy);
      font: 400 calc(var(--size, 44px) * .34) / 1 var(--display);
    }
    /* League headshots are wide with the face in the upper middle. */
    img { width: 100%; height: 100%; object-fit: cover; object-position: 50% 10%; background: #fff; }
  `,
})
export class Avatar {
  readonly src = input<string | null | undefined>(null);
  readonly name = input('');

  protected readonly failedSrc = signal<string | null>(null);
  protected readonly failed = computed(() => this.failedSrc() === this.src());
  protected readonly initials = computed(() =>
    this.name()
      .split(/\s+/)
      .filter((part) => /^\p{L}/u.test(part))
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join('') || '?',
  );
}
