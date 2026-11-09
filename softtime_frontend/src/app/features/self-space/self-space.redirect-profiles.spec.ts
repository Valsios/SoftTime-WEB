import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { AuthService } from '../../shared/services/auth.service';
import { LoginPage } from '../auth/login';

/**
 * Revue de non-regression : destination apres connexion pour CHAQUE profil existant (administrateur, chaque porte seule, portes
 * 3 a 6, compte sans permission, droits d une seule porte accompagnes de permissions detaillees). Tous restent sur /dashboard ;
 * seul un compte SELF:READ sans aucune porte arrive sur /mon-espace.
 */
describe('Revue : destination apres connexion, tous profils existants', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  function seConnecter(permissions: string[], rights: number[], roleId = 2) {
    const reponse = {
      token: 'jeton', userId: 1, login: 'x', nom: 'X', roleId, matricule: null, rights, authorizedDatabases: [], permissions, permissionsCustomized: true,
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isFirstRun: () => of(false),
            login: () => {
              TestBed.inject(SessionStore).setSession(reponse);
              return of(reponse);
            },
          },
        },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const page = TestBed.createComponent(LoginPage).componentInstance;
    page.login.set('x');
    page.password.set('y');
    page.submit();
    return navigate;
  }

  const profilsExistants: { nom: string; permissions: string[]; portes: number[]; role?: number }[] = [
    { nom: 'administrateur (portes 1 a 6)', permissions: [], portes: [1, 2, 3, 4, 5, 6], role: 1 },
    { nom: 'porte 1 seule', permissions: ['SAGE:EMPLOYEES'], portes: [1] },
    { nom: 'porte 2 seule', permissions: ['PARAMS:SETTINGS'], portes: [2] },
    { nom: 'porte 3 seule', permissions: ['PROC:SHIFTS'], portes: [3] },
    { nom: 'porte 4 seule', permissions: ['OTHER:USERS'], portes: [4] },
    { nom: 'porte 5 seule', permissions: ['TRACE:LOGS'], portes: [5] },
    { nom: 'porte 6 seule', permissions: ['REPORTS:REPORTS'], portes: [6] },
    { nom: 'portes 3 a 6', permissions: ['PROC:SHIFTS', 'OTHER:USERS', 'TRACE:LOGS', 'REPORTS:REPORTS'], portes: [3, 4, 5, 6] },
    { nom: 'porte seule sans permission detaillee (droit de role)', permissions: [], portes: [2] },
    { nom: 'compte sans permission ni porte', permissions: [], portes: [] },
    { nom: 'permission detaillee sans porte deduite (autre que SELF:READ)', permissions: ['PROC:SHIFTS'], portes: [] },
  ];

  for (const p of profilsExistants) {
    it(`${p.nom} : /dashboard, strictement inchange`, () => {
      expect(seConnecter(p.permissions, p.portes, p.role ?? 2)).toHaveBeenCalledWith(['/dashboard']);
    });
  }

  it('SELF:READ avec chacune des portes 1 a 6 : /dashboard (le profil Salarie exige l absence de toute porte)', () => {
    for (const porte of [1, 2, 3, 4, 5, 6]) {
      TestBed.resetTestingModule();
      expect(seConnecter(['SELF:READ'], [porte])).toHaveBeenCalledWith(['/dashboard']);
    }
  });

  it('seul SELF:READ sans aucune porte arrive sur /mon-espace', () => {
    expect(seConnecter(['SELF:READ'], [])).toHaveBeenCalledWith(['/mon-espace']);
  });
});
