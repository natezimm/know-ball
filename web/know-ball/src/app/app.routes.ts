import { Routes } from '@angular/router';

const scoutingReport = () => import('./games/scouting-report/scouting-report').then((m) => m.ScoutingReport);
const careerPath = () => import('./games/career-path/career-path').then((m) => m.CareerPath);

// A game's bare address is today's puzzle; "/puzzle/:puzzleId" is one from the archive or a practice round.
export const routes: Routes = [
  {
    path: '',
    title: 'Know Ball — Daily Sports Trivia',
    loadComponent: () => import('./pages/home/home').then((m) => m.Home),
  },
  {
    path: 'spordle',
    title: 'Spordle — Know Ball',
    loadComponent: () => import('./games/spordle/spordle').then((m) => m.Spordle),
  },
  { path: 'scouting-report', title: 'Scouting Report — Know Ball', loadComponent: scoutingReport },
  { path: 'scouting-report/puzzle/:puzzleId', title: 'Scouting Report — Know Ball', loadComponent: scoutingReport },
  { path: 'career-path', title: 'Career Path — Know Ball', loadComponent: careerPath },
  { path: 'career-path/puzzle/:puzzleId', title: 'Career Path — Know Ball', loadComponent: careerPath },
  {
    path: 'mockups',
    title: 'Mockups — Know Ball',
    loadChildren: () => import('./mockups/mockups.routes').then((m) => m.MOCKUP_ROUTES),
  },
  { path: '**', redirectTo: '' },
];
