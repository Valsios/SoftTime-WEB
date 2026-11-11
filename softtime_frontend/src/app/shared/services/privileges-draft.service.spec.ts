import { TestBed } from '@angular/core/testing';
import { signal, WritableSignal } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionStore } from '../../core/session.store';
import { PrivilegesDraftService } from './privileges-draft.service';

describe('PrivilegesDraftService', () => {
  function create(session: WritableSignal<{ userId: number } | null> = signal({ userId: 1 })) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        PrivilegesDraftService,
        { provide: SessionStore, useValue: { session } },
      ],
    });
    return TestBed.inject(PrivilegesDraftService);
  }

  beforeEach(() => {
    vi.spyOn(Storage.prototype, 'getItem');
    vi.spyOn(Storage.prototype, 'setItem');
    vi.spyOn(Storage.prototype, 'removeItem');
    vi.spyOn(Storage.prototype, 'clear');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('détecte un brouillon modifié', () => {
    const service = create();

    service.loadBaseline('user', 4, ['users.read']);
    service.update('user', 4, ['roles.read']);

    expect(service.hasDirty('user', 4)).toBe(true);
  });

  it('compte les droits ajoutés et retirés', () => {
    const service = create();
    service.loadBaseline('user', 4, ['users.read', 'roles.read']);

    service.update('user', 4, ['users.read', 'db-access.read', 'privileges.read']);

    expect(service.hasDirty()).toBe(true);
    expect(service.hasDirty('user', 4)).toBe(true);
    expect(service.changesCount('user', 4)).toBe(3);
  });

  it("n'est plus modifié quand l'état revient à la base", () => {
    const service = create();
    service.loadBaseline('user', 4, ['users.read']);
    service.update('user', 4, ['roles.read']);

    service.update('user', 4, ['users.read']);

    expect(service.hasDirty('user', 4)).toBe(false);
    expect(service.changesCount('user', 4)).toBe(0);
  });

  it('discard remet le brouillon à la base', () => {
    const service = create();
    service.loadBaseline('role', 2, ['users.read']);
    service.update('role', 2, ['roles.read', 'db-access.read']);

    service.discard('role', 2);

    expect(service.hasDirty('role', 2)).toBe(false);
    expect(service.changesCount('role', 2)).toBe(0);
    expect(service.get('role', 2)?.working).toEqual(new Set(['users.read']));
  });

  it('markSaved aligne la base sur le brouillon', () => {
    const service = create();
    service.loadBaseline('user', 7, ['users.read']);
    service.update('user', 7, ['roles.read']);

    service.markSaved('user', 7);

    expect(service.hasDirty('user', 7)).toBe(false);
    expect(service.changesCount('user', 7)).toBe(0);
    expect(service.get('user', 7)?.baseline).toEqual(new Set(['roles.read']));
  });

  it('clearAll vide tous les brouillons', () => {
    const service = create();
    service.loadBaseline('user', 1, ['users.read']);
    service.loadBaseline('role', 2, ['roles.read']);
    service.update('user', 1, ['roles.read']);

    service.clearAll();

    expect(service.get('user', 1)).toBeUndefined();
    expect(service.get('role', 2)).toBeUndefined();
    expect(service.hasDirty()).toBe(false);
  });

  it('garde les clés mode:targetId indépendantes', () => {
    const service = create();
    service.loadBaseline('user', 1, ['users.read']);
    service.loadBaseline('role', 1, ['roles.read']);

    service.update('user', 1, ['users.read', 'roles.read']);

    expect(service.hasDirty('user', 1)).toBe(true);
    expect(service.hasDirty('role', 1)).toBe(false);
    expect(service.changesCount('role', 1)).toBe(0);

    service.clearTarget('user', 1);

    expect(service.get('user', 1)).toBeUndefined();
    expect(service.hasDirty('role', 1)).toBe(false);
  });

  it("se vide quand l'identifiant de session change", () => {
    const session = signal<{ userId: number } | null>({ userId: 1 });
    const service = create(session);
    service.loadBaseline('user', 3, ['users.read']);
    service.update('user', 3, ['roles.read']);

    session.set({ userId: 2 });
    TestBed.tick();

    expect(service.hasDirty()).toBe(false);
    expect(service.get('user', 3)).toBeUndefined();
  });

  it("n'utilise ni localStorage ni sessionStorage", () => {
    const service = create();
    service.loadBaseline('user', 5, ['users.read']);
    service.update('user', 5, ['roles.read']);
    service.discard('user', 5);
    service.markSaved('user', 5);
    service.clearTarget('user', 5);
    service.clearAll();

    const calls = [
      ...vi.mocked(Storage.prototype.getItem).mock.calls,
      ...vi.mocked(Storage.prototype.setItem).mock.calls,
      ...vi.mocked(Storage.prototype.removeItem).mock.calls,
      ...vi.mocked(Storage.prototype.clear).mock.calls,
    ];

    expect(calls).toHaveLength(0);
  });

  it('refreshBaseline sans brouillon crée le brouillon', () => {
    const service = create();
    const res = service.refreshBaseline('user', 10, ['users.read']);

    expect(res.dirty).toBe(false);
    expect(res.serverChanged).toBe(false);
    expect(res.baseline).toEqual(new Set(['users.read']));
    expect(res.working).toEqual(new Set(['users.read']));
    expect(service.get('user', 10)?.working).toEqual(new Set(['users.read']));
  });

  it('refreshBaseline sur brouillon non modifié adopte la nouvelle base serveur', () => {
    const service = create();
    service.loadBaseline('user', 10, ['users.read']);

    const res = service.refreshBaseline('user', 10, ['users.read', 'roles.read']);

    expect(res.dirty).toBe(false);
    expect(res.serverChanged).toBe(false);
    expect(res.baseline).toEqual(new Set(['users.read', 'roles.read']));
    expect(res.working).toEqual(new Set(['users.read', 'roles.read']));
  });

  it('refreshBaseline sur brouillon modifié sans changement serveur garde working avec dirty=true et serverChanged=false', () => {
    const service = create();
    service.loadBaseline('user', 10, ['users.read']);
    service.update('user', 10, ['users.read', 'roles.read']);

    const res = service.refreshBaseline('user', 10, ['users.read']);

    expect(res.dirty).toBe(true);
    expect(res.serverChanged).toBe(false);
    expect(res.baseline).toEqual(new Set(['users.read']));
    expect(res.working).toEqual(new Set(['users.read', 'roles.read']));
  });

  it('refreshBaseline sur brouillon modifié avec changement serveur garde working avec dirty=true et serverChanged=true', () => {
    const service = create();
    service.loadBaseline('user', 10, ['users.read']);
    service.update('user', 10, ['users.read', 'roles.read']);

    const res = service.refreshBaseline('user', 10, ['users.read', 'db-access.read']);

    expect(res.dirty).toBe(true);
    expect(res.serverChanged).toBe(true);
    expect(res.baseline).toEqual(new Set(['users.read', 'db-access.read']));
    expect(res.working).toEqual(new Set(['users.read', 'roles.read']));
  });

  it('markSaved avec savedPermissions aligne la base et conserve working', () => {
    const service = create();
    service.loadBaseline('user', 10, ['users.read']);
    service.update('user', 10, ['users.read', 'roles.read', 'db-access.read']);

    service.markSaved('user', 10, ['users.read', 'roles.read']);

    expect(service.get('user', 10)?.baseline).toEqual(new Set(['users.read', 'roles.read']));
    expect(service.get('user', 10)?.working).toEqual(new Set(['users.read', 'roles.read', 'db-access.read']));
    expect(service.hasDirty('user', 10)).toBe(true);
  });

  it('markSaved et update sans brouillon ne lèvent aucune exception', () => {
    const service = create();

    expect(() => service.markSaved('user', 99)).not.toThrow();
    expect(() => service.markSaved('user', 99, ['users.read'])).not.toThrow();
    expect(() => service.update('user', 99, ['users.read'])).not.toThrow();
  });

  it('setActive, getActive et clearAll gèrent la cible active', () => {
    const service = create();
    expect(service.getActive()).toBeNull();

    service.setActive('role', 5);
    expect(service.getActive()).toEqual({ mode: 'role', targetId: 5 });

    service.clearAll();
    expect(service.getActive()).toBeNull();
  });

  it('dirtyTargets liste les brouillons modifiés', () => {
    const service = create();
    service.loadBaseline('user', 1, ['users.read']);
    service.loadBaseline('user', 2, ['users.read']);
    service.loadBaseline('role', 3, ['roles.read']);

    service.update('user', 1, ['roles.read']);
    service.update('role', 3, ['users.read']);

    const dirty = service.dirtyTargets();
    expect(dirty).toHaveLength(2);
    expect(dirty).toContainEqual({ mode: 'user', targetId: 1 });
    expect(dirty).toContainEqual({ mode: 'role', targetId: 3 });
  });
});
