import { Routes } from '@angular/router';
import { GeoSportsMock } from './games/geo-sports';
import { GoatMock } from './games/goat';
import { SoundItOutMock } from './games/sound-it-out';
import { SportleMock } from './games/sportle';
import { StatQuizMock } from './games/stat-quiz';
import { Mockups } from './mockups';
import { ThemesMock } from './themes/themes';

// Paths match MOCK_GAMES in mock-data.ts. The version 1 games were built and their sketches removed.
export const MOCKUP_ROUTES: Routes = [
  {
    path: '',
    component: Mockups,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'themes' },
      { path: 'themes', component: ThemesMock },
      { path: 'stat-quiz', component: StatQuizMock },
      { path: 'geo-sports', component: GeoSportsMock },
      { path: 'sportle', component: SportleMock },
      { path: 'sound-it-out', component: SoundItOutMock },
      { path: 'goat', component: GoatMock },
    ],
  },
];
