import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Droit } from './rights';
import { SessionStore } from './session.store';

/** SessionStore.permissions() et hasRight() : source des décisions de droit des écrans (dont l'écran des bases SAGE). */
describe('SessionStore : permissions et portes', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });
  afterEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  // Jeton sans claim lisible : les portes viennent alors du champ « rights » de la réponse de connexion.
  const connecter = (roleId: number, rights: number[], permissions?: string[]) => {
    const store = TestBed.inject(SessionStore);
    store.setSession({
      token: 'jeton-illisible', userId: 1, login: 'x', nom: 'X', roleId, matricule: null, rights, authorizedDatabases: [],
      permissions: permissions as string[], permissionsCustomized: !!permissions?.length,
    });
    return store;
  };

  it('session non connectée : aucune permission, aucune porte', () => {
    const store = TestBed.inject(SessionStore);

    expect(store.permissions()).toEqual([]);
    expect(store.hasRight(Droit.Databases)).toBe(false);
  });

  it('liste de permissions absente dans la réponse : liste vide', () => {
    const store = connecter(2, [Droit.Databases], undefined);

    expect(store.permissions()).toEqual([]);
  });

  it('liste fournie : rendue telle quelle', () => {
    const store = connecter(2, [Droit.Databases], ['SAGE:CREATE', 'SAGE:BASE_A:UPDATE']);

    expect(store.permissions()).toEqual(['SAGE:CREATE', 'SAGE:BASE_A:UPDATE']);
  });

  it('porte présente ou absente (compte non administrateur)', () => {
    const store = connecter(2, [Droit.Databases]);

    expect(store.hasRight(Droit.Databases)).toBe(true);
    expect(store.hasRight(Droit.Parameters)).toBe(false);
  });

  it('administrateur (rôle 1) : toutes les portes, même sans droit listé', () => {
    const store = connecter(1, []);

    expect(store.hasRight(Droit.Databases)).toBe(true);
    expect(store.hasRight(Droit.Reports)).toBe(true);
  });

  it('après déconnexion : plus de permission ni de porte', () => {
    const store = connecter(2, [Droit.Databases], ['SAGE:CREATE']);
    store.clear();

    expect(store.permissions()).toEqual([]);
    expect(store.hasRight(Droit.Databases)).toBe(false);
  });
});
