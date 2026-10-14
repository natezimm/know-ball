export type ShareOutcome = 'shared' | 'copied' | 'failed';

/**
 * Hands a result to the phone's share sheet where there is one, and otherwise copies it.
 * Desktop browsers that offer a share sheet too still get a copy: it is what people expect there.
 */
export async function shareText(text: string): Promise<ShareOutcome> {
  const touch = typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches;
  if (touch && typeof navigator.share === 'function') {
    try {
      await navigator.share({ text });
      return 'shared';
    } catch (error) {
      // Closing the sheet is not a failure; anything else falls through to copying.
      if (error instanceof DOMException && error.name === 'AbortError') return 'shared';
    }
  }

  try {
    await navigator.clipboard.writeText(text);
    return 'copied';
  } catch {
    return 'failed';
  }
}

/** "Scouting Report #142 4/8", "… X/8" for a loss, "… practice 4/8" off the calendar. */
export function shareHeadline(name: string, number: number | null, guesses: number, max: number, won: boolean): string {
  const which = number === null ? 'practice' : `#${number}`;
  return `Know Ball · ${name} ${which} ${won ? guesses : 'X'}/${max}`;
}
