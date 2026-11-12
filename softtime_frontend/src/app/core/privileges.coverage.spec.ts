import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { selfSpaceGuard } from '../features/self-space/self-space.guard';
import { canEditRolePermissions, isAdminRole, isInternalMarker } from '../features/params/privileges.logic';
import { AccessContext, canAccess, decodeTokenGates, gateAllows, hasUsableDatabase, permissionAllows } from './access.logic';
import { authGuard, databaseGuard, droitGuard } from './guards';
import { Droit } from './rights';
import { SessionStore } from './session.store';

/**
 * Couverture complémentaire des privilèges côté écran : gardes de route, matrice canAccess / permissionAllows / gateAllows,
 * jetons mal formés, règles de la page Privilèges non couvertes ailleurs, et différences connues avec le serveur (documentées).
 */

/** Jeton non signé : seul le contenu compte pour l'écran (le serveur vérifie la signature). */
function jeton(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) => btoa(JSON.stringify(o)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  return `${b64({ alg: 'none' })}.${b64(payload)}.sig`;
}

function ctx(over: Partial<AccessContext> = {}): AccessContext {
  return { roleId: 2, permissions: [], customized: false, gates: [], fallbackRights: [], ...over };
}

describe('Privilèges : matrice permissionAllows / gateAllows / canAccess', () => {
  // [cas, contexte, code, porte, permission seule, porte seule, accès complet]
  const cas: [string, AccessContext, string | undefined, number | undefined, boolean, boolean, boolean][] = [
    ['administrateur, rien dans le jeton', ctx({ roleId: 1, gates: [] }), 'PARAMS:USERS', Droit.Parameters, true, true, true],
    ['liste vide, porte présente : la porte décide', ctx({ gates: [Droit.Parameters] }), 'PARAMS:USERS', Droit.Parameters, true, true, true],
    ['liste vide, porte absente', ctx({ gates: [] }), 'PARAMS:USERS', Droit.Parameters, false, false, false],
    ['liste non vide avec le code, porte absente', ctx({ permissions: ['PARAMS:USERS'], gates: [] }), 'PARAMS:USERS', Droit.Parameters, true, false, false],
    ['liste non vide sans le code, porte présente', ctx({ permissions: ['PARAMS:ROLES'], gates: [Droit.Parameters] }), 'PARAMS:USERS', Droit.Parameters, false, true, false],
    ['compte personnalisé avec liste vide : refus même avec la porte', ctx({ customized: true, gates: [Droit.Parameters] }), 'PARAMS:USERS', Droit.Parameters, false, true, false],
    ['code « SAGE » : n’importe quel droit SAGE:*', ctx({ permissions: ['SAGE:BASE_A:READ'], gates: [Droit.Databases] }), 'SAGE', Droit.Databases, true, true, true],
    ['code « POINTEUSE » sans droit pointeuse', ctx({ permissions: ['SAGE:BASE_A:READ'], gates: [Droit.Databases] }), 'POINTEUSE', Droit.Databases, false, true, false],
    ['ni code ni porte demandés', ctx(), undefined, undefined, true, true, true],
    ['jeton illisible : repli sur les portes de la connexion', ctx({ gates: null, fallbackRights: [Droit.Reports] }), 'REPORTS:REPORTS', Droit.Reports, true, true, true],
    ['casse : le code est comparé tel quel', ctx({ permissions: ['params:users'], gates: [Droit.Parameters] }), 'PARAMS:USERS', Droit.Parameters, false, true, false],
  ];

  for (const [nom, c, code, porte, perm, gate, complet] of cas) {
    it(nom, () => {
      expect(permissionAllows(c, code, porte)).toBe(perm);
      expect(gateAllows(c, porte)).toBe(gate);
      expect(canAccess(c, code, porte)).toBe(complet);
    });
  }
});

describe('Privilèges : lecture des portes dans le jeton', () => {
  it('jetons mal formés ou sans portes', () => {
    expect(decodeTokenGates('pas-un-jeton')).toBeNull();                 // une seule partie
    expect(decodeTokenGates('a.!!!.c')).toBeNull();                      // contenu non décodable
    expect(decodeTokenGates(jeton({ rights: null }))).toEqual([]);
    expect(decodeTokenGates(jeton({ rights: ' 1 , x, 0, -2, 3.5, 6 ' }))).toEqual([1, 6]);   // seuls les entiers positifs
    expect(decodeTokenGates(jeton({ rights: 4 }))).toEqual([4]);
  });

  it('base utilisable : au moins une base avec un nom', () => {
    expect(hasUsableDatabase(null)).toBe(false);
    expect(hasUsableDatabase([])).toBe(false);
    expect(hasUsableDatabase([{ id: 1, nomBd: '' } as never])).toBe(false);
    expect(hasUsableDatabase([{ id: 1, nomBd: 'BASE_A' } as never])).toBe(true);
  });
});

describe('Privilèges : gardes de route', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  const connecter = (roleId: number, portes: number[], permissions: string[], bases: string[] = []) =>
    TestBed.inject(SessionStore).setSession({
      token: jeton({ rights: portes.join(',') }), userId: 1, login: 'x', nom: 'X', roleId, matricule: null, rights: portes,
      authorizedDatabases: bases.map((b, i) => ({ id: i + 1, nomBd: b }) as never), permissions, permissionsCustomized: permissions.length > 0,
    });
  const lancer = (garde: typeof authGuard, data: Record<string, unknown> = {}) =>
    TestBed.runInInjectionContext(() => garde({ data } as never, {} as never));
  const url = (r: unknown) => (r instanceof UrlTree ? TestBed.inject(Router).serializeUrl(r) : r);

  it('authGuard : non connecté vers /login, connecté passe', () => {
    expect(url(lancer(authGuard))).toBe('/login');
    connecter(2, [], []);
    expect(lancer(authGuard)).toBe(true);
  });

  // [cas, rôle, portes, permissions, données de route, résultat]
  const droitCas: [string, number, number[], string[], Record<string, unknown>, true | string][] = [
    ['route sans exigence', 2, [], [], {}, true],
    ['porte présente, liste vide', 2, [Droit.Parameters], [], { droit: Droit.Parameters, permission: 'PARAMS:USERS' }, true],
    ['porte absente', 2, [Droit.Reports], [], { droit: Droit.Parameters, permission: 'PARAMS:USERS' }, '/'],
    ['permission sans la porte', 2, [], ['PARAMS:USERS'], { droit: Droit.Parameters, permission: 'PARAMS:USERS' }, '/'],
    ['porte sans la permission (liste non vide)', 2, [Droit.Parameters], ['PARAMS:ROLES'], { droit: Droit.Parameters, permission: 'PARAMS:USERS' }, '/'],
    ['administrateur', 1, [], [], { droit: Droit.Databases, permission: 'SAGE' }, true],
    ['compte Salarié (SELF:READ, aucune porte)', 2, [], ['SELF:READ'], { droit: Droit.Processing, permission: 'PROC:SHIFTS' }, '/'],
  ];

  for (const [nom, role, portes, perms, data, attendu] of droitCas) {
    it(`droitGuard : ${nom}`, () => {
      connecter(role, portes, perms);
      expect(url(lancer(droitGuard, data))).toBe(attendu);
    });
  }

  it('databaseGuard : sans base utilisable vers /no-database, avec une base passe', () => {
    connecter(2, [Droit.Databases], []);
    expect(url(lancer(databaseGuard))).toBe('/no-database');
    connecter(2, [Droit.Databases], [], ['BASE_A']);
    expect(lancer(databaseGuard)).toBe(true);
  });

  it('selfSpaceGuard : le Salarié passe, l’administrateur sans SELF:READ est renvoyé à l’accueil', () => {
    connecter(2, [], ['SELF:READ']);
    expect(lancer(selfSpaceGuard)).toBe(true);
    connecter(1, [1, 2, 3, 4, 5, 6], []);
    expect(url(lancer(selfSpaceGuard))).toBe('/');
  });
});

describe('Privilèges : règles de la page Privilèges', () => {
  it('marqueurs internes : jamais des droits', () => {
    expect(isInternalMarker('__SOFTTIME_EMPTY_PERMISSIONS__')).toBe(true);
    expect(isInternalMarker('__SOFTTIME_EMPTY_ROLE__')).toBe(true);
    expect(isInternalMarker('SAGE:BASE_A:READ')).toBe(false);
    expect(isInternalMarker(' __SOFTTIME_EMPTY_X')).toBe(false);          // comportement actuel : pas de nettoyage des espaces
  });

  // [rôle de la session, rôle visé, modifiable]
  const roles: [number | null, number | null, boolean][] = [
    [1, 5, true], [1, 1, false], [1, null, false], [5, 6, false], [null, 5, false], [5, 1, false],
  ];
  for (const [session, cible, ok] of roles) {
    it(`canEditRolePermissions : session ${session}, rôle ${cible} -> ${ok}`, () => {
      expect(canEditRolePermissions(session, cible)).toBe(ok);
    });
  }

  it('isAdminRole : seul l’identifiant 1', () => {
    expect([1, 0, 2, null, undefined].map((r) => isAdminRole(r))).toEqual([true, false, false, false, false]);
  });
});

describe('Cohérence écran / serveur : différences connues, documentées sans correction', () => {
  it('casse des codes : l’écran compare tel quel, le serveur sans tenir compte de la casse (DatabaseConfigurationAuthorizer)', () => {
    expect(permissionAllows(ctx({ permissions: ['sage:base_a:update'] }), 'SAGE:BASE_A:UPDATE')).toBe(false);
  });

  it('liste non vide sans porte Bases : l’écran accepte la permission seule, le serveur exige aussi la porte', () => {
    expect(permissionAllows(ctx({ permissions: ['SAGE:BASE_A:UPDATE'], gates: [] }), 'SAGE:BASE_A:UPDATE', Droit.Databases)).toBe(true);
    expect(canAccess(ctx({ permissions: ['SAGE:BASE_A:UPDATE'], gates: [] }), 'SAGE:BASE_A:UPDATE', Droit.Databases)).toBe(false);
  });

  it('administrateur : canAccess le laisse toujours passer (comme le serveur) ; l’écran des bases, lui, ne fait pas d’exception (voir sage-databases.rights.spec.ts)', () => {
    expect(canAccess(ctx({ roleId: 1 }), 'SAGE:NOUVELLE:UPDATE', Droit.Databases)).toBe(true);
  });
});
