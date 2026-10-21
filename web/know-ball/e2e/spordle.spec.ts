import { Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, repoRoot, test } from './support';

type Status = 'correct' | 'present' | 'absent';

/** The answer list the server draws from, by category. */
const bank: Record<string, string> = JSON.parse(
  readFileSync(resolve(repoRoot, 'src/KnowBall.Application/Games/Spordle/Data/sports-words.json'), 'utf8'),
);

test.beforeEach(async ({ page }) => {
  await page.goto('/spordle');
  await expect(page.getByRole('status').filter({ hasText: 'Guess 1 of 6' })).toBeVisible();
});

/**
 * The server picks the word at random, so this plays it the way a person would: take both hints
 * (they cost nothing), then guess only words that fit every score so far. With the category and
 * one placed letter, every word in the bank is found within four guesses.
 */
test('the hints and the scores lead to the word, and a win is recorded', async ({ page }) => {
  const category = await revealHint(page, 'Category', 'Reveal category');
  const letter = (await revealHint(page, 'Letter', 'Reveal letter')).match(/^Letter (\d) is ([A-Z])$/)!;
  const [position, placed] = [Number(letter[1]) - 1, letter[2].toLowerCase()];

  let candidates = bank[category].split(' ').filter((w) => w.length === 5 && w[position] === placed);
  expect(candidates.length).toBeGreaterThan(0);

  for (let guesses = 1; ; guesses++) {
    const guess = candidates[0];
    const score = await submit(page, guess);
    await expect(page.getByRole('group', { name: new RegExp(`^Guess ${guesses}: ${guess[0]} ${score[0]}`) })).toBeVisible();
    if (score.every((s) => s === 'correct')) {
      await expect(page.locator('.outcome')).toContainText(`Solved in ${guesses} ${guesses === 1 ? 'guess' : 'guesses'}`);
      break;
    }
    candidates = candidates.filter((w) => scoreOf(w, guess).join() === score.join());
    expect(guesses).toBeLessThan(6);
  }
  await expect(page.getByRole('status').filter({ hasText: 'Solved' })).toBeVisible();

  await page.getByRole('button', { name: 'Stats' }).click();
  const stats = page.getByRole('dialog', { name: /Statistics/ });
  await expect(stats.locator('.stats div', { hasText: 'Played' }).locator('dd')).toHaveText('1');
  await expect(stats.locator('.stats div', { hasText: 'Win %' }).locator('dd')).toHaveText('100%');
});

test('a made-up word costs nothing, six misses lose, and play again starts fresh', async ({ page }) => {
  await page.keyboard.type('zzzzz');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Not in the word list. Try again.')).toBeVisible();
  for (let i = 0; i < 5; i++) await page.keyboard.press('Backspace');
  await expect(page.getByRole('status').filter({ hasText: 'Guess 1 of 6' })).toBeVisible();

  // Everyday words from the dictionary; none of them is a sports answer.
  for (const word of ['about', 'other', 'which', 'their', 'there', 'would']) await submit(page, word);

  const outcome = page.locator('.outcome');
  await expect(outcome).toContainText('The word was');
  const answer = (await outcome.locator('strong').textContent())!.trim().toLowerCase();
  expect(Object.values(bank).join(' ').split(' ')).toContain(answer);
  await expect(page.getByRole('group', { name: /^Guess 6: w / })).toBeVisible();

  await outcome.getByRole('button', { name: 'Play again' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Guess 1 of 6' })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Guess 1: empty' })).toBeVisible();

  await page.getByRole('button', { name: 'Stats' }).click();
  const stats = page.getByRole('dialog', { name: /Statistics/ });
  await expect(stats.locator('.stats div', { hasText: 'Played' }).locator('dd')).toHaveText('1');
  await expect(stats.locator('.stats div', { hasText: 'Win %' }).locator('dd')).toHaveText('0%');
});

test('retrying a lost response scores the row only once on the real API', async ({ page }) => {
  let requestId = '';
  await page.route('**/api/spordle/games/*/guesses', async (route) => {
    requestId = route.request().postDataJSON().requestId;
    const committed = await route.fetch();
    expect(committed.ok()).toBe(true);
    expect((await committed.json()).attemptsRemaining).toBe(5);
    // Commit the guess, then drop its response before the browser receives it.
    await route.abort('failed');
  }, { times: 1 });
  await page.keyboard.type('about');
  await page.keyboard.press('Enter');
  await expect(page.getByText('Error checking the word. Please try again.')).toBeVisible();
  const retryResponse = page.waitForResponse((r) => /\/guesses$/.test(r.url()));
  await page.keyboard.press('Enter');
  const retry = await retryResponse;
  expect(retry.request().postDataJSON().requestId).toBe(requestId);
  expect((await retry.json()).attemptsRemaining).toBe(5);
  await expect(page.getByRole('status').filter({ hasText: 'Guess 2 of 6' })).toBeVisible();
  await submit(page, 'other');
  await expect(page.getByRole('status').filter({ hasText: 'Guess 3 of 6' })).toBeVisible();
});

test('the word length can be changed and is remembered', async ({ page }) => {
  await page.getByRole('button', { name: '4 letters' }).click();
  await expect(page.getByRole('button', { name: '4 letters' })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'm', exact: true }).click();
  for (const key of ['i', 't', 't']) await page.getByRole('button', { name: key, exact: true }).click();
  await expect(page.getByRole('group', { name: 'Guess 1: mitt' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('button', { name: '4 letters' })).toHaveAttribute('aria-pressed', 'true');
});

/** Opens a hint, reveals it and returns its text as the hint button then shows it. */
async function revealHint(page: Page, button: string, reveal: string): Promise<string> {
  await page.getByRole('button', { name: button, exact: true }).click();
  const dialog = page.locator('dialog[open]');
  await dialog.getByRole('button', { name: reveal }).click();
  await dialog.getByRole('button', { name: 'Got it' }).click();
  await expect(dialog).toHaveCount(0);
  return (await page.locator('.hints .btn.hint.revealed').last().textContent())!.trim();
}

/** Types a word, submits it and returns the server's score for it. */
async function submit(page: Page, word: string): Promise<Status[]> {
  await page.keyboard.type(word);
  const response = page.waitForResponse((r) => r.request().method() === 'POST' && /\/guesses$/.test(r.url()));
  await page.keyboard.press('Enter');
  const body = await (await response).json();
  expect(body.valid).toBe(true);
  return body.score;
}

/** Wordle scoring, as the server does it: repeated letters are only marked present while unmatched. */
function scoreOf(answer: string, guess: string): Status[] {
  const score: Status[] = [...guess].map((c, i) => (c === answer[i] ? 'correct' : 'absent'));
  const unmatched = new Map<string, number>();
  [...answer].forEach((c, i) => score[i] !== 'correct' && unmatched.set(c, (unmatched.get(c) ?? 0) + 1));
  [...guess].forEach((c, i) => {
    if (score[i] === 'correct' || !unmatched.get(c)) return;
    score[i] = 'present';
    unmatched.set(c, unmatched.get(c)! - 1);
  });
  return score;
}
