import { Routes } from '@angular/router';
import { authGuard, droitGuard } from './core/guards';
import { Droit } from './core/rights';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login').then((m) => m.LoginPage),
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },

      // Databases (droit 1)
      {
        path: 'sage-databases',
        canActivate: [droitGuard],
        data: { droit: Droit.Databases },
        loadComponent: () =>
          import('./features/params/sage-databases').then((m) => m.SageDatabasesPage),
      },
      {
        path: 'pointeuse-databases',
        canActivate: [droitGuard],
        data: { droit: Droit.Databases },
        loadComponent: () =>
          import('./features/params/pointeuse-databases').then((m) => m.PointeuseDatabasesPage),
      },

      // Parameters (droit 2)
      {
        path: 'users',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/users').then((m) => m.UsersPage),
      },
      {
        path: 'roles',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/roles').then((m) => m.RolesPage),
      },
      {
        path: 'privileges',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/privileges').then((m) => m.PrivilegesPage),
      },
      {
        path: 'db-access',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/db-access').then((m) => m.DbAccessPage),
      },
      {
        path: 'clock-params',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/clock-params').then((m) => m.ClockParamsPage),
      },
      {
        path: 'correspondence',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () =>
          import('./features/params/correspondence').then((m) => m.CorrespondencePage),
      },
      {
        path: 'cardpaie',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/cardpaie').then((m) => m.CardPaiePage),
      },
      {
        path: 'categories',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/categories').then((m) => m.CategoriesPage),
      },
      {
        path: 'affectations',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () =>
          import('./features/params/affectations').then((m) => m.AffectationsPage),
      },
      {
        path: 'tolerances',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/tolerances').then((m) => m.TolerancesPage),
      },
      {
        path: 'holidays',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/holidays').then((m) => m.HolidaysPage),
      },
      {
        path: 'majorations',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () => import('./features/params/majorations').then((m) => m.MajorationsPage),
      },
      {
        path: 'code-constantes',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () =>
          import('./features/params/code-constantes').then((m) => m.CodeConstantesPage),
      },
      {
        path: 'absence-codes',
        canActivate: [droitGuard],
        data: { droit: Droit.Parameters },
        loadComponent: () =>
          import('./features/params/absence-codes').then((m) => m.AbsenceCodesPage),
      },

      // Processing (droit 3)
      {
        path: 'shifts',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () => import('./features/processing/shifts').then((m) => m.ShiftsPage),
      },
      {
        path: 'employee-planning',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () =>
          import('./features/processing/employee-planning').then((m) => m.EmployeePlanningPage),
      },
      {
        path: 'punches',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () => import('./features/processing/punches').then((m) => m.PunchesPage),
      },
      {
        path: 'anomalies',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () => import('./features/processing/anomalies').then((m) => m.AnomaliesPage),
      },
      {
        path: 'corrected-hours',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () =>
          import('./features/processing/corrected-hours').then((m) => m.CorrectedHoursPage),
      },
      {
        path: 'weekly-validation',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () =>
          import('./features/processing/weekly-validation').then((m) => m.WeeklyValidationPage),
      },
      {
        path: 'overtime',
        canActivate: [droitGuard],
        data: { droit: Droit.Processing },
        loadComponent: () => import('./features/processing/overtime').then((m) => m.OvertimePage),
      },

      // Reports (droit 6)
      {
        path: 'reports',
        canActivate: [droitGuard],
        data: { droit: Droit.Reports },
        loadComponent: () => import('./features/reports/reports').then((m) => m.ReportsPage),
      },

      // Extra (droit 4)
      {
        path: 'canteen',
        canActivate: [droitGuard],
        data: { droit: Droit.Other },
        loadComponent: () => import('./features/reports/canteen').then((m) => m.CanteenPage),
      },
      {
        path: 'travel-expenses',
        canActivate: [droitGuard],
        data: { droit: Droit.Other },
        loadComponent: () => import('./features/reports/travel').then((m) => m.TravelPage),
      },

      // Traceability (droit 5)
      {
        path: 'audit',
        canActivate: [droitGuard],
        data: { droit: Droit.Traceability },
        loadComponent: () => import('./features/reports/audit').then((m) => m.AuditPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
