import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { PointeuseDatabasesPage } from './pointeuse-databases';

const REFUS_MODIF = "Vous n'avez pas la permission de modifier cette base pointeuse.";
const REFUS_AJOUT = "Vous n'avez pas la permission d'ajouter une base pointeuse.";
const REFUS_SUPPR = "Vous n'avez pas la permission de supprimer cette base pointeuse.";

/** Page sans rendu : décisions de droit et appels au service seulement (le serveur reste l'autorité). */
function monter(permissions: string[] | null, porteBases = false) {
  TestBed.resetTestingModule();
  const svc = {
    update: vi.fn(() => of({})),
    create: vi.fn(() => of({})),
    remove: vi.fn(() => of(undefined)),
    refresh: vi.fn(() => of([])),
    listPaged: vi.fn(() => of({ items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 })),
  };
  const toast = { error: vi.fn(), success: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      { provide: PointeuseDatabasesService, useValue: svc },
      { provide: SessionStore, useValue: { permissions: () => permissions, hasRight: (d: number) => porteBases && d === Droit.Databases } },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: { ask: async () => true } },
    ],
  });
  const page = TestBed.runInInjectionContext(() => new PointeuseDatabasesPage());
  return { page, svc, toast };
}

const ligne = { id: 3, serveur: 'SRV', nomBd: 'PTG_ORIGINE', typeBase: 'STANDARD', sqlAuth: true, active: true };

describe("Bases pointeuse : droit vérifié sur le nom d'origine lors d'une modification", () => {
  afterEach(() => TestBed.resetTestingModule());

  it("renommer vers un nom sans permission, avec la permission sur le nom d'origine : la sauvegarde est envoyée", () => {
    const { page, svc, toast } = monter(['POINTEUSE:PTG_ORIGINE:UPDATE']);

    page.openEdit(ligne);
    page.onSaved({ ...ligne, nomBd: 'NOUVEAU_NOM' });

    expect(svc.update).toHaveBeenCalledTimes(1);
    expect(toast.error).not.toHaveBeenCalled();
  });

  it("sans permission sur le nom d'origine : refusé même si le nouveau nom est autorisé", () => {
    const { page, svc, toast } = monter(['POINTEUSE:PTG_ORIGINE:READ', 'POINTEUSE:AUTORISE:UPDATE']);

    page.editId.set(3);
    page.editOriginalName.set('PTG_ORIGINE');
    page.onSaved({ ...ligne, nomBd: 'AUTORISE' });

    expect(svc.update).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(REFUS_MODIF);
  });

  it("après une sauvegarde réussie, le nom d'origine est remis à null", () => {
    const { page } = monter(['POINTEUSE:PTG_ORIGINE:UPDATE']);

    page.openEdit(ligne);
    page.onSaved({ ...ligne, nomBd: 'NOUVEAU_NOM' });

    expect(page.modal()).toBe(false);
    expect(page.editOriginalName()).toBeNull();
  });

  it("erreur serveur : message clair du serveur, jamais l'objet d'erreur brut ; la fenêtre reste ouverte", () => {
    const { page, svc, toast } = monter(['POINTEUSE:PTG_ORIGINE:UPDATE']);
    svc.update.mockReturnValueOnce(
      throwError(() => ({ status: 409, error: { success: false, message: 'Cette base (serveur + nom) est déjà enregistrée.' } })) as never,
    );

    page.openEdit(ligne);
    page.onSaved({ ...ligne, nomBd: 'DOUBLON' });

    expect(toast.error).toHaveBeenCalledWith('Cette base (serveur + nom) est déjà enregistrée.');
    expect(page.modal()).toBe(true);
    expect(page.editOriginalName()).toBe('PTG_ORIGINE');
  });
});

describe('Bases pointeuse : boutons selon les droits', () => {
  afterEach(() => TestBed.resetTestingModule());

  // [profil, permissions, porte Bases, ajouter, modifier PTG_ORIGINE, supprimer PTG_ORIGINE]
  const cas: [string, string[] | null, boolean, boolean, boolean, boolean][] = [
    ['compte hérité (aucune permission fine) avec la porte Bases', [], true, true, true, true],
    ['compte hérité sans la porte Bases', [], false, false, false, false],
    ['lecture seule de la base', ['POINTEUSE:PTG_ORIGINE:READ'], true, false, false, false],
    ['ajout seulement', ['POINTEUSE:CREATE'], true, true, false, false],
    ['modification et suppression de cette base', ['POINTEUSE:PTG_ORIGINE:UPDATE', 'POINTEUSE:PTG_ORIGINE:DELETE'], true, false, true, true],
    ['droits sur une autre base seulement', ['POINTEUSE:AUTRE:UPDATE', 'POINTEUSE:AUTRE:DELETE'], true, false, false, false],
  ];

  for (const [nom, perms, porte, ajouter, modifier, supprimer] of cas) {
    it(nom, () => {
      const { page } = monter(perms, porte);
      expect(page.canCreate()).toBe(ajouter);
      expect(page.canUpdate(ligne)).toBe(modifier);
      expect(page.canDelete(ligne)).toBe(supprimer);
    });
  }

  it('suppression refusée sans le droit : message clair et aucune requête', async () => {
    const { page, svc, toast } = monter(['POINTEUSE:PTG_ORIGINE:READ'], true);

    await page.remove(ligne);

    expect(svc.remove).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(REFUS_SUPPR);
  });

  it("ajout refusé sans le droit : la fenêtre ne s'ouvre pas", () => {
    const { page, toast } = monter(['POINTEUSE:PTG_ORIGINE:READ'], true);

    page.openCreate();

    expect(page.modal()).toBe(false);
    expect(toast.error).toHaveBeenCalledWith(REFUS_AJOUT);
  });
});
