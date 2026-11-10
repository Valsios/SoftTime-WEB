import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { CatalogDomain } from '../../shared/models';
import { AuthService } from '../../shared/services/auth.service';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { PrivilegesService, RolesService, SageDatabasesService, UsersService } from '../../shared/services/users.service';
import { PrivilegesPage } from './privileges';

/**
 * Matrice de l'écran Privilèges (page sans rendu, services doublés) : groupes par domaine (catalogue du serveur ou
 * liste locale), recherche, indicateur « modifications non enregistrées ».
 */

const CATALOG: CatalogDomain[] = [
  { key: 'PARAMS', gate: 2, codes: ['PARAMS:HOLIDAYS', 'PARAMS:HOLIDAYS:CREATE'] },
  { key: 'PROC', gate: 3, codes: ['PROC:SHIFTS', 'PROC:SHIFTS:CREATE', 'PROC:SHIFTS:DELETE', 'PROC:PUNCHES'] },
  { key: 'SELF', gate: 0, codes: ['SELF:READ'] },
];

function monter(catalog: () => unknown = () => of(CATALOG)) {
  TestBed.resetTestingModule();
  const priv = {
    catalog: vi.fn(catalog),
    getRolePermissions: vi.fn(() => of({ roleId: 3, grantedPermissions: ['PROC:SHIFTS'], isConfigured: true })),
    byUser: vi.fn(() => of({ userId: 0, grantedPermissions: [], isCustomized: false })),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: UsersService, useValue: { list: () => of([]) } },
      { provide: RolesService, useValue: { list: () => of([{ id: 3, nom: 'Gestionnaire' }]) } },
      { provide: PrivilegesService, useValue: priv },
      { provide: SageDatabasesService, useValue: { list: () => of([]) } },
      { provide: PointeuseDatabasesService, useValue: { list: () => of([]) } },
      { provide: SessionStore, useValue: { session: () => ({ userId: 1, roleId: 1 }), hasPermission: () => true } },
      { provide: AuthService, useValue: { refreshMe: () => of({}) } },
      { provide: ToastService, useValue: { error: vi.fn(), success: vi.fn(), info: vi.fn(), warning: vi.fn() } },
      { provide: ConfirmService, useValue: { ask: async () => true } },
    ],
  });
  const page = TestBed.runInInjectionContext(() => new PrivilegesPage());
  page.ngOnInit();
  return { page, priv };
}

const coche = (checked: boolean) => ({ target: { checked } }) as unknown as Event;

describe('Privilèges : matrice par domaine', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('groupes construits depuis le catalogue du serveur, avec les libellés locaux et « Mon espace »', () => {
    const { page } = monter();

    expect(page.featureCategories.map((c) => c.key)).toEqual(['PARAMS', 'PROC', 'SELF']);
    const proc = page.featureCategories.find((c) => c.key === 'PROC')!;
    expect(proc.title).toBe('Traitements');
    expect(proc.items.find((i) => i.code === 'PROC:SHIFTS')).toMatchObject({ label: 'Shifts', canCreate: true, canDelete: true });
    expect(page.featureCategories.find((c) => c.key === 'SELF')!.items.map((i) => i.code)).toEqual(['SELF:READ']);
  });

  it('catalogue indisponible : la liste locale reste affichée (Mon espace compris)', () => {
    const { page } = monter(() => throwError(() => ({ status: 500 })));

    const keys = page.featureCategories.map((c) => c.key);
    expect(keys).toEqual(['PARAMS', 'PROC', 'OTHER', 'TRACE', 'REPORTS', 'SELF']);
  });

  it('recherche : seules les catégories et pages qui correspondent sont affichées', () => {
    const { page } = monter();

    page.search.set('shift');

    expect(page.visibleCategories().map((c) => c.key)).toEqual(['PROC']);
    expect(page.visibleCategories()[0].items.map((i) => i.code)).toEqual(['PROC:SHIFTS']);
    page.search.set('');
    expect(page.visibleCategories().length).toBe(3);
  });

  it('indicateur « modifications non enregistrées » : allumé après une case cochée, éteint si on la décoche', () => {
    const { page } = monter();
    page.setMode('role');
    page.onRoleChange(3);
    expect(page.isDirty()).toBe(false);

    page.toggle('PROC:PUNCHES', coche(true));
    expect(page.isDirty()).toBe(true);
    expect(page.changesCount()).toBe(1);

    page.toggle('PROC:PUNCHES', coche(false));
    expect(page.isDirty()).toBe(false);
  });
});
