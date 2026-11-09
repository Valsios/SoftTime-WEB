import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { SourceMappingStepper } from '../../shared/components';
import { AuthService } from '../../shared/services/auth.service';
import { DiscoveryService, FieldRolesService, PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { SageDatabasesService } from '../../shared/services/users.service';
import { SageDatabasesPage } from './sage-databases';

/**
 * Droits de l'écran des bases SAGE (frontend) : matrice canCreate / canUpdate / canDelete et parcours réels
 * (openEdit, onSaved, closeModal, openCreate, remove), sans poser les signaux à la main. Le serveur reste l'autorité.
 */

const REFUS_MODIF = "Vous n'avez pas la permission de modifier cette base SAGE.";
const REFUS_AJOUT = "Vous n'avez pas la permission d'ajouter une base SAGE.";
const REFUS_SUPPR = "Vous n'avez pas la permission de supprimer cette base SAGE.";

type Permissions = string[] | null | undefined;

function doublures(permissions: Permissions, porteBases: boolean) {
  const svc = {
    update: vi.fn(() => of({})),
    create: vi.fn(() => of({})),
    remove: vi.fn(() => of(undefined)),
    listPaged: vi.fn(() => of({ items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 })),
    testConnection: vi.fn(() => of({ success: true, message: '' })),
  };
  const toast = { error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() };
  const providers = [
    { provide: SageDatabasesService, useValue: svc },
    { provide: AuthService, useValue: { refreshMe: () => of({}) } },
    { provide: SessionStore, useValue: { permissions: () => permissions, hasRight: (d: number) => porteBases && d === Droit.Databases } },
    { provide: ToastService, useValue: toast },
    { provide: ConfirmService, useValue: { ask: async () => true } },
  ];
  return { svc, toast, providers };
}

/** Page sans rendu : décisions de droit et appels au service seulement. */
function page(permissions: Permissions, porteBases = false) {
  TestBed.resetTestingModule();
  const d = doublures(permissions, porteBases);
  TestBed.configureTestingModule({ providers: d.providers });
  return { ...d, page: TestBed.runInInjectionContext(() => new SageDatabasesPage()) };
}

const A = { id: 3033, serveur: 'SRV', nomBd: 'BASE_A', typeBase: 'STANDARD', sqlAuth: true };
const B = { id: 3048, serveur: 'SRV', nomBd: 'BASE_B', typeBase: 'STANDARD', sqlAuth: true };

describe('Bases SAGE : matrice des droits de l\'écran', () => {
  afterEach(() => TestBed.resetTestingModule());

  // [profil, permissions, porte Bases, canCreate, canUpdate(A), canDelete(A)]
  const cas: [string, Permissions, boolean, boolean, boolean, boolean][] = [
    ['liste vide, porte Bases', [], true, true, true, true],
    ['liste vide, sans porte Bases', [], false, false, false, false],
    ['liste null, porte Bases', null, true, true, true, true],
    ['liste undefined, sans porte Bases', undefined, false, false, false, false],
    ['administrateur : CREATE + droits par base', ['SAGE:CREATE', 'SAGE:BASE_A:READ', 'SAGE:BASE_A:UPDATE', 'SAGE:BASE_A:DELETE'], true, true, true, true],
    ['UPDATE sans DELETE', ['SAGE:BASE_A:READ', 'SAGE:BASE_A:UPDATE'], true, false, true, false],
    ['DELETE sans UPDATE', ['SAGE:BASE_A:READ', 'SAGE:BASE_A:DELETE'], true, false, false, true],
    ['READ seul', ['SAGE:BASE_A:READ'], true, false, false, false],
    ['CREATE seul', ['SAGE:CREATE'], true, true, false, false],
    ['droits sur une autre base seulement', ['SAGE:BASE_B:UPDATE', 'SAGE:BASE_B:DELETE'], true, false, false, false],
    // La porte n'est consultée que pour une liste vide : une liste non vide sans porte suffit côté écran (le serveur exige la porte).
    ['liste non vide, sans porte Bases', ['SAGE:BASE_A:UPDATE'], false, false, true, false],
  ];

  for (const [profil, perms, porte, creer, modifier, supprimer] of cas) {
    it(`${profil} : création ${creer}, modification ${modifier}, suppression ${supprimer}`, () => {
      const { page: p } = page(perms, porte);

      expect(p.canCreate()).toBe(creer);
      expect(p.canUpdate(A)).toBe(modifier);
      expect(p.canDelete(A)).toBe(supprimer);
    });
  }

  it('comportement actuel : le nom est comparé tel quel (casse et espaces comptent)', () => {
    const { page: p } = page(['SAGE:BASE_A:UPDATE', 'SAGE:BASE_A:DELETE']);

    expect(p.canUpdate({ ...A, nomBd: 'base_a' })).toBe(false);
    expect(p.canUpdate({ ...A, nomBd: ' BASE_A ' })).toBe(false);
    expect(p.canDelete({ ...A, nomBd: 'Base_A' })).toBe(false);
  });

  it('nomBd absent ou vide : refusé pour une liste non vide, porte Bases pour une liste vide', () => {
    const avecListe = page(['SAGE:BASE_A:UPDATE', 'SAGE:BASE_A:DELETE']).page;
    expect(avecListe.canUpdate({ id: 1 })).toBe(false);
    expect(avecListe.canUpdate({ id: 1, nomBd: '' })).toBe(false);
    expect(avecListe.canDelete({ id: 1, nomBd: null })).toBe(false);

    const sansListe = page([], true).page;
    expect(sansListe.canUpdate({ id: 1 })).toBe(true);
    expect(sansListe.canDelete({ id: 1, nomBd: '' })).toBe(true);
  });

  it('CREATE par base (« SAGE:<nom>:CREATE ») suffit aussi pour la création', () => {
    expect(page(['SAGE:BASE_A:CREATE']).page.canCreate()).toBe(true);
  });
});

describe('Bases SAGE : parcours réels de l\'écran', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renommage : droit sur le nom d\'origine seulement, la sauvegarde part avec le nouveau nom', () => {
    const { page: p, svc, toast } = page(['SAGE:BASE_A:UPDATE']);

    p.openEdit(A);
    p.onSaved({ ...A, nomBd: 'NOUVEAU' });

    expect(svc.update).toHaveBeenCalledOnce();
    expect(svc.update).toHaveBeenCalledWith(3033, expect.objectContaining({ nomBd: 'NOUVEAU' }));
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('sans droit sur le nom d\'origine : la fenêtre ne s\'ouvre pas, rien n\'est envoyé', () => {
    const { page: p, svc, toast } = page(['SAGE:BASE_B:UPDATE']);

    p.openEdit(A);
    p.onSaved({ ...A, nomBd: 'BASE_A' });

    expect(p.modal()).toBe(false);
    expect(p.editId()).toBeNull();
    expect(toast.error).toHaveBeenCalledWith(REFUS_MODIF);
    expect(svc.update).not.toHaveBeenCalled();
    expect(svc.create).not.toHaveBeenCalled();
  });

  it('droit sur le nouveau nom mais pas sur l\'ancien : refusé, même si le droit a été retiré après l\'ouverture', () => {
    let perms = ['SAGE:BASE_A:UPDATE', 'SAGE:BASE_B:UPDATE'];
    TestBed.resetTestingModule();
    const d = doublures(null, false);
    TestBed.configureTestingModule({
      providers: d.providers.map((x) => (x.provide === SessionStore ? { provide: SessionStore, useValue: { permissions: () => perms, hasRight: () => false } } : x)),
    });
    const p = TestBed.runInInjectionContext(() => new SageDatabasesPage());

    p.openEdit(A);
    perms = ['SAGE:BASE_B:UPDATE'];                    // droit sur l'ancien nom retiré (nouvelle session), droit sur le nouveau nom
    p.onSaved({ ...A, nomBd: 'BASE_B' });

    expect(d.svc.update).not.toHaveBeenCalled();
    expect(d.toast.error).toHaveBeenCalledWith(REFUS_MODIF);
  });

  it('ouvrir la ligne A puis la ligne B : le droit est vérifié sur le nom de B', () => {
    const { page: p, svc } = page(['SAGE:BASE_A:UPDATE', 'SAGE:BASE_B:UPDATE']);

    p.openEdit(A);
    p.closeModal();
    p.openEdit(B);
    expect(p.editOriginalName()).toBe('BASE_B');
    expect(p.editId()).toBe(3048);
    p.onSaved({ ...B, nomBd: 'RENOMMEE' });

    expect(svc.update).toHaveBeenCalledWith(3048, expect.objectContaining({ nomBd: 'RENOMMEE' }));
  });

  it('openCreate remet editId et editOriginalName à null, sans passer par closeModal', () => {
    const { page: p, svc } = page(['SAGE:CREATE', 'SAGE:BASE_A:UPDATE']);

    p.openEdit(A);
    p.openCreate();

    expect(p.editId()).toBeNull();
    expect(p.editOriginalName()).toBeNull();
    p.onSaved({ serveur: 'SRV', nomBd: 'NOUVELLE' });
    expect(svc.create).toHaveBeenCalledWith(expect.objectContaining({ id: 0, nomBd: 'NOUVELLE' }));
    expect(svc.update).not.toHaveBeenCalled();
  });

  it('création sans SAGE:CREATE : refusée à l\'ouverture et à l\'enregistrement, rien n\'est envoyé', () => {
    const { page: p, svc, toast } = page(['SAGE:BASE_A:UPDATE']);

    p.openCreate();
    p.onSaved({ serveur: 'SRV', nomBd: 'NOUVELLE' });

    expect(p.modal()).toBe(false);
    expect(toast.error).toHaveBeenCalledWith(REFUS_AJOUT);
    expect(svc.create).not.toHaveBeenCalled();
  });

  it('suppression sans SAGE:<nom>:DELETE : refusée, aucune requête', async () => {
    const { page: p, svc, toast } = page(['SAGE:BASE_A:UPDATE']);

    await p.remove(A);

    expect(toast.error).toHaveBeenCalledWith(REFUS_SUPPR);
    expect(svc.remove).not.toHaveBeenCalled();
  });

  it('suppression avec SAGE:<nom>:DELETE : la requête part avec l\'identifiant de la ligne', async () => {
    const { page: p, svc } = page(['SAGE:BASE_A:DELETE']);

    await p.remove(A);

    expect(svc.remove).toHaveBeenCalledWith(3033);
  });

  it('erreur serveur : la fenêtre reste ouverte et le nom d\'origine reste le bon', () => {
    const { page: p, svc } = page(['SAGE:BASE_A:UPDATE']);
    svc.update.mockReturnValueOnce(throwError(() => ({ status: 409, error: { success: false, message: 'Cette base (serveur + nom) est déjà enregistrée.' } })) as never);

    p.openEdit(A);
    p.onSaved({ ...A, nomBd: 'DOUBLON' });

    expect(p.modal()).toBe(true);
    expect(p.saving()).toBe(false);
    expect(p.editOriginalName()).toBe('BASE_A');
    p.onSaved({ ...A, nomBd: 'AUTRE' });              // nouvel essai dans la même fenêtre : toujours contrôlé sur BASE_A
    expect(svc.update).toHaveBeenCalledTimes(2);
  });

  it('comportement actuel après une sauvegarde réussie : la fenêtre se ferme mais editOriginalName n\'est pas remis à null', () => {
    const { page: p } = page(['SAGE:BASE_A:UPDATE']);

    p.openEdit(A);
    p.onSaved({ ...A, nomBd: 'NOUVEAU' });

    expect(p.modal()).toBe(false);
    // Documenté, non corrigé : sans effet visible, car openEdit et openCreate le réécrivent avant toute autre sauvegarde.
    expect(p.editOriginalName()).toBe('BASE_A');
    expect(p.editId()).toBe(3033);
  });
});

describe('Bases SAGE : lien formulaire -> page', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('le formulaire émet la sauvegarde avec le nom saisi et la page envoie la mise à jour, contrôlée sur le nom d\'origine', () => {
    TestBed.resetTestingModule();
    const d = doublures(['SAGE:BASE_A:UPDATE'], true);
    TestBed.configureTestingModule({
      providers: [
        ...d.providers,
        provideRouter([]),
        { provide: DiscoveryService, useValue: { databases: () => of([]), tables: () => of([]), columns: () => of([]), servers: () => of([]) } },
        { provide: FieldRolesService, useValue: { list: () => of([]) } },
        { provide: PointeuseDatabasesService, useValue: { testConnection: () => of({ success: true, message: '' }) } },
      ],
    });
    const fixture = TestBed.createComponent(SageDatabasesPage);
    fixture.detectChanges();
    const p = fixture.componentInstance;

    p.openEdit(A);
    fixture.detectChanges();
    const formulaire = fixture.debugElement.query(By.directive(SourceMappingStepper)).componentInstance as SourceMappingStepper;
    formulaire.onBaseChange('NOUVEAU');
    formulaire.save();

    expect(d.svc.update).toHaveBeenCalledOnce();
    expect(d.svc.update).toHaveBeenCalledWith(3033, expect.objectContaining({ id: 3033, nomBd: 'NOUVEAU', serveur: 'SRV', typeBase: 'STANDARD' }));
    expect(d.toast.error).not.toHaveBeenCalled();
  });
});
