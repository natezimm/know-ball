import { Routes } from '@angular/router';
import { PageMetaData } from './core/page-meta';

const scoutingReport = () => import('./games/scouting-report/scouting-report').then((m) => m.ScoutingReport);
const careerPath = () => import('./games/career-path/career-path').then((m) => m.CareerPath);

// A game's bare address is today's puzzle; "/puzzle/:puzzleId" is one from the archive or a practice round.
// Search engines index each game's bare address and skip archive puzzles and the design lab (core/page-meta.ts).
const archive: PageMetaData = { noindex: true };

export const routes: Routes = [
  {
    path: 'numbers-dont-lie',
    title: "Numbers Don't Lie — Know Ball",
    data: {
      description: "Numbers Don't Lie (beta): five questions about stats, awards, teams and past seasons. Choose your leagues in practice.",
    } satisfies PageMetaData,
    loadComponent: () => import('./games/numbers-dont-lie/numbers-dont-lie').then(m => m.NumbersDontLie),
  },
  {
    path: '',
    title: 'Know Ball — Daily Sports Trivia',
    loadComponent: () => import('./pages/home/home').then((m) => m.Home),
  },
  {
    path: 'spordle',
    title: 'Spordle — Know Ball',
    data: { description: 'Spordle: six guesses to find a sports word of 4, 5 or 6 letters.' } satisfies PageMetaData,
    loadComponent: () => import('./games/spordle/spordle').then((m) => m.Spordle),
  },
  {
    path: 'scouting-report',
    title: 'Scouting Report — Know Ball',
    data: {
      description: 'Scouting Report: guess the player in 8 tries with team, position, height, age and number clues. A new puzzle every day.',
    } satisfies PageMetaData,
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
  { path: 'career-path/puzzle/:puzzleId', title: 'Career Path — Know Ball', data: archive, loadComponent: careerPath },
  {
    path: 'mockups',
    title: 'Mockups — Know Ball',
    data: { noindex: true } satisfies PageMetaData,
    loadChildren: () => import('./mockups/mockups.routes').then((m) => m.MOCKUP_ROUTES),
  },
  { path: '**', redirectTo: '' },
];
