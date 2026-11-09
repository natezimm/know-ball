import { Routes } from '@angular/router';
import { PageMetaData } from './core/page-meta';

const scoutingReport = () => import('./games/scouting-report/scouting-report').then((m) => m.ScoutingReport);
const careerPath = () => import('./games/career-path/career-path').then((m) => m.CareerPath);
const backOfTheCard = () => import('./games/back-of-the-card/back-of-the-card').then((m) => m.BackOfTheCard);

// A game's bare address is today's puzzle; "/puzzle/:puzzleId" is one from the archive or a practice round.
// Search engines index each game's bare address and skip archive puzzles and the design lab (core/page-meta.ts).
const archive: PageMetaData = { noindex: true };

export const routes: Routes = [
  {
    path: 'numbers-dont-lie',
    title: "Numbers Don't Lie — Know Ball",
    data: {
      description: "Numbers Don't Lie: five questions about sports records, nicknames, famous numbers and history. Choose your leagues in practice.",
    } satisfies PageMetaData,
    loadComponent: () => import('./games/numbers-dont-lie/numbers-dont-lie').then(m => m.NumbersDontLie),
  },
  {
    path: '',
    title: 'Know Ball — Daily Sports Trivia',
    loadComponent: () => import('./pages/home/home').then((m) => m.Home),
  },
  {
    path: 'letter-jacket',
    title: 'Letter Jacket — Know Ball',
    data: { description: 'Letter Jacket: six guesses to find a sports word of 4, 5 or 6 letters.' } satisfies PageMetaData,
    loadComponent: () => import('./games/letter-jacket/letter-jacket').then((m) => m.LetterJacket),
  },
  {
    path: 'scouting-report',
    title: 'Scouting Report — Know Ball',
    data: {
      description: 'Scouting Report: guess the player in 8 tries with team, position, height, age and number clues. A new puzzle every day.',
    } satisfies PageMetaData,
    loadComponent: scoutingReport,
  },
  {
    path: 'scouting-report/nfl',
    title: 'Scouting Report NFL — Know Ball',
    data: {
      league: 'nfl',
      description: 'Scouting Report NFL: guess the NFL player in 8 tries with team, position, height, age and number clues. A new puzzle every day.',
    } satisfies PageMetaData & { league: 'nfl' },
    loadComponent: scoutingReport,
  },
  { path: 'scouting-report/puzzle/:puzzleId', title: 'Scouting Report — Know Ball', data: archive, loadComponent: scoutingReport },
  {
    path: 'career-path',
    title: 'Career Path — Know Ball',
    data: {
      description: 'Career Path: every team they played for, in order. Who is it? A new puzzle every day.',
    } satisfies PageMetaData,
    loadComponent: careerPath,
  },
  {
    path: 'career-path/nfl',
    title: 'Career Path NFL — Know Ball',
    data: {
      league: 'nfl',
      description: 'Career Path NFL: every team an NFL player suited up for, in order. Who is it? A new puzzle every day.',
    } satisfies PageMetaData & { league: 'nfl' },
    loadComponent: careerPath,
  },
  { path: 'career-path/puzzle/:puzzleId', title: 'Career Path — Know Ball', data: archive, loadComponent: careerPath },
  {
    path: 'back-of-the-card',
    title: 'Back of the Card — Know Ball',
    data: {
      description: "Back of the Card: every season's stats from a player's card, name blacked out. Who is it? A new puzzle every day.",
    } satisfies PageMetaData,
    loadComponent: backOfTheCard,
  },
  {
    path: 'back-of-the-card/nfl',
    title: 'Back of the Card NFL — Know Ball',
    data: {
      league: 'nfl',
      description: "Back of the Card NFL: every season's totals from an NFL player's card, name blacked out. Who is it? A new puzzle every day.",
    } satisfies PageMetaData & { league: 'nfl' },
    loadComponent: backOfTheCard,
  },
  { path: 'back-of-the-card/puzzle/:puzzleId', title: 'Back of the Card — Know Ball', data: archive, loadComponent: backOfTheCard },
  {
    path: 'mockups',
    title: 'Mockups — Know Ball',
    data: { noindex: true } satisfies PageMetaData,
    loadChildren: () => import('./mockups/mockups.routes').then((m) => m.MOCKUP_ROUTES),
  },
  { path: '**', redirectTo: '' },
];
