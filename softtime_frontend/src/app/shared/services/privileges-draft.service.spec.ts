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

    service.loadBaseline('user', 4, ['PARAMS:USERS']);
    service.update('user', 4, ['PARAMS:ROLES']);

    expect(service.hasDirty('user', 4)).toBe(true);
  });

  it('compte les droits ajoutés et retirés', () => {
    const service = create();
    service.loadBaseline('user', 4, ['PARAMS:USERS', 'PARAMS:ROLES']);

    service.update('user', 4, ['PARAMS:USERS', 'PARAMS:DB_ACCESS', 'PARAMS:PRIVILEGES']);

    expect(service.hasDirty()).toBe(true);
    expect(service.hasDirty('user', 4)).toBe(true);
    expect(service.changesCount('user', 4)).toBe(3);
  });

  it("n'est plus modifié quand l'état revient à la base", () => {
    const service = create();
    service.loadBaseline('user', 4, ['PARAMS:USERS']);
    service.update('user', 4, ['PARAMS:ROLES']);

    service.update('user', 4, ['PARAMS:USERS']);

    expect(service.hasDirty('user', 4)).toBe(false);
    expect(service.changesCount('user', 4)).toBe(0);
  });

  it('discard remet le brouillon à la base', () => {
    const service = create();
    service.loadBaseline('role', 2, ['PARAMS:USERS']);
    service.update('role', 2, ['PARAMS:ROLES', 'PARAMS:DB_ACCESS']);

    service.discard('role', 2);

    expect(service.hasDirty('role', 2)).toBe(false);
    expect(service.changesCount('role', 2)).toBe(0);
    expect(service.get('role', 2)?.working).toEqual(new Set(['PARAMS:USERS']));
  });

  it('markSaved aligne la base sur le brouillon', () => {
    const service = create();
    service.loadBaseline('user', 7, ['PARAMS:USERS']);
    service.update('user', 7, ['PARAMS:ROLES']);

    service.markSaved('user', 7);

    expect(service.hasDirty('user', 7)).toBe(false);
    expect(service.changesCount('user', 7)).toBe(0);
    expect(service.get('user', 7)?.baseline).toEqual(new Set(['PARAMS:ROLES']));
  });

  it('clearAll vide tous les brouillons', () => {
    const service = create();
    service.loadBaseline('user', 1, ['PARAMS:USERS']);
    service.loadBaseline('role', 2, ['PARAMS:ROLES']);
    service.update('user', 1, ['PARAMS:ROLES']);

    service.clearAll();

    expect(service.get('user', 1)).toBeUndefined();
    expect(service.get('role', 2)).toBeUndefined();
    expect(service.hasDirty()).toBe(false);
  });

  it('garde les clés mode:targetId indépendantes', () => {
    const service = create();
    service.loadBaseline('user', 1, ['PARAMS:USERS']);
    service.loadBaseline('role', 1, ['PARAMS:ROLES']);

    service.update('user', 1, ['PARAMS:USERS', 'PARAMS:ROLES']);

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
    service.loadBaseline('user', 3, ['PARAMS:USERS']);
    service.update('user', 3, ['PARAMS:ROLES']);

    session.set({ userId: 2 });
    TestBed.tick();

    expect(service.hasDirty()).toBe(false);
    expect(service.get('user', 3)).toBeUndefined();
  });

  it("n'utilise ni localStorage ni sessionStorage", () => {
    const service = create();
    service.loadBaseline('user', 5, ['PARAMS:USERS']);
    service.update('user', 5, ['PARAMS:ROLES']);
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
});
