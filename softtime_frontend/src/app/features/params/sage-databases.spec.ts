import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ConfirmService } from '../../core/confirm.service';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { AuthService } from '../../shared/services/auth.service';
import { SageDatabasesService } from '../../shared/services/users.service';
import { SageDatabasesPage } from './sage-databases';

const REFUS_MODIF = "Vous n'avez pas la permission de modifier cette base SAGE.";
const REFUS_AJOUT = "Vous n'avez pas la permission d'ajouter une base SAGE.";

/** Page sans rendu (le formulaire n'est pas affiché) : seules les décisions de droit et les appels au service sont observés. */
function monter(permissions: string[], porteBases = false) {
  TestBed.resetTestingModule();
  const svc = {
    update: vi.fn(() => of({})),
    create: vi.fn(() => of({})),
    listPaged: vi.fn(() => of({ items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 })),
  };
  const toast = { error: vi.fn(), success: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      { provide: SageDatabasesService, useValue: svc },
      { provide: AuthService, useValue: { refreshMe: () => of({}) } },
      { provide: SessionStore, useValue: { permissions: () => permissions, hasRight: (d: number) => porteBases && d === Droit.Databases } },
      { provide: ToastService, useValue: toast },
      { provide: ConfirmService, useValue: { ask: async () => true } },
    ],
  });
  const page = TestBed.runInInjectionContext(() => new SageDatabasesPage());
  return { page, svc, toast };
}

const ligne = { id: 3033, serveur: 'SRV', nomBd: 'BASE_ORIGINE', typeBase: 'STANDARD', sqlAuth: true };

describe('Bases SAGE : droit vérifié sur le nom d\'origine lors d\'une modification', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renommer vers un nom sans permission, avec la permission sur le nom d\'origine : la sauvegarde est envoyée', () => {
    const { page, svc, toast } = monter(['SAGE:BASE_ORIGINE:UPDATE', 'SAGE:CREATE']);

    page.openEdit(ligne);
    page.onSaved({ ...ligne, nomBd: 'NOUVEAU_NOM' });

    expect(svc.update).toHaveBeenCalledTimes(1);
    expect(svc.update).toHaveBeenCalledWith(3033, expect.objectContaining({ nomBd: 'NOUVEAU_NOM' }));   // le nom saisi part au serveur
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('sans la permission sur le nom d\'origine : refus, même si le nom saisi est autorisé, et aucune requête', () => {
    const { page, svc, toast } = monter(['SAGE:AUTRE_BASE:UPDATE']);

    page.editId.set(3033);                                   // modification ouverte sur une ligne dont le droit a disparu
    page.editOriginalName.set('BASE_ORIGINE');
    page.onSaved({ ...ligne, nomBd: 'AUTRE_BASE' });

    expect(svc.update).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(REFUS_MODIF);
  });

  it('modifier sans renommer : inchangé, la sauvegarde est envoyée', () => {
    const { page, svc } = monter(['SAGE:BASE_ORIGINE:UPDATE']);

    page.openEdit(ligne);
    page.onSaved({ ...ligne, serveur: 'AUTRE_SERVEUR' });

    expect(svc.update).toHaveBeenCalledTimes(1);
  });

  it('liste de permissions vide : la porte Bases décide, comme avant', () => {
    const avecPorte = monter([], true);
    avecPorte.page.openEdit(ligne);
    avecPorte.page.onSaved({ ...ligne, nomBd: 'NOUVEAU_NOM' });
    expect(avecPorte.svc.update).toHaveBeenCalledTimes(1);

    const sansPorte = monter([], false);
    sansPorte.page.editId.set(3033);
    sansPorte.page.editOriginalName.set('BASE_ORIGINE');
    sansPorte.page.onSaved({ ...ligne, nomBd: 'NOUVEAU_NOM' });
    expect(sansPorte.svc.update).not.toHaveBeenCalled();
    expect(sansPorte.toast.error).toHaveBeenCalledWith(REFUS_MODIF);
  });

  it('la création garde son contrôle : SAGE:CREATE exigé, le nom d\'une modification précédente est oublié', () => {
    const refuse = monter(['SAGE:BASE_ORIGINE:UPDATE']);
    refuse.page.openCreate();
    expect(refuse.toast.error).toHaveBeenCalledWith(REFUS_AJOUT);
    refuse.page.onSaved({ serveur: 'SRV', nomBd: 'NOUVELLE' });
    expect(refuse.svc.create).not.toHaveBeenCalled();

    const autorise = monter(['SAGE:CREATE', 'SAGE:BASE_ORIGINE:UPDATE']);
    autorise.page.openEdit(ligne);
    autorise.page.closeModal();
    expect(autorise.page.editOriginalName()).toBeNull();
    autorise.page.openCreate();
    expect(autorise.page.editId()).toBeNull();
    expect(autorise.page.editOriginalName()).toBeNull();
    autorise.page.onSaved({ serveur: 'SRV', nomBd: 'NOUVELLE' });
    expect(autorise.svc.create).toHaveBeenCalledWith(expect.objectContaining({ id: 0, nomBd: 'NOUVELLE' }));
    expect(autorise.svc.update).not.toHaveBeenCalled();
  });
});
