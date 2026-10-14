export type LetterStatus = 'correct' | 'present' | 'absent';

const PRIORITY: Record<LetterStatus, number> = { absent: 1, present: 2, correct: 3 };

/**
 * Folds a scored guess into the keyboard's colours. A key only ever moves up:
 * once a letter is known to be correct, a later "present" for it changes nothing.
 */
export function mergeKeyboardStatuses(
  current: Readonly<Record<string, LetterStatus>>,
  guess: string,
  score: readonly LetterStatus[],
): Record<string, LetterStatus> {
  const next = { ...current };
  [...guess].forEach((letter, i) => {
    const previous = next[letter];
    if (!previous || PRIORITY[score[i]] > PRIORITY[previous]) next[letter] = score[i];
  });
  return next;
}
