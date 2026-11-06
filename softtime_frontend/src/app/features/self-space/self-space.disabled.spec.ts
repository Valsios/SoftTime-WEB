import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { throwError } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { SelfSpacePage } from './self-space';
import { SelfSpaceService } from './self-space.service';
import { describeSelfError, strongestNotice } from './self-space.errors';

const SERVEUR = 'Mon espace n\'est pas activé sur ce serveur.';

function monter(erreur: unknown) {
  TestBed.resetTestingModule();
  const lecture = () => throwError(() => erreur);
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: SelfSpaceService, useValue: { weeks: lecture, anomalies: lecture, absences: lecture, delays: lecture } }],
  });
  const fixture = TestBed.createComponent(SelfSpacePage);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Mon espace : option coupée sur le serveur (404)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('un seul encadré clair remplace les quatre cartes en erreur', () => {
    const el = monter({ status: 404, error: { error: SERVEUR } });

    expect(el.querySelectorAll('.self-notice')).toHaveLength(1);
    expect(el.querySelector('.self-notice')?.textContent?.trim()).toBe('Mon espace n\'est pas encore activé : contactez la RH.');
    expect(el.querySelector('.self-notice')?.getAttribute('data-kind')).toBe('disabled');
    expect(el.querySelectorAll('h2')).toHaveLength(0);
    expect(el.textContent).not.toContain('Ces données n’ont pas pu être chargées.');
    expect(el.textContent).not.toContain('sur ce serveur');                     // le message technique du serveur n'est pas répété
    expect(el.textContent).not.toContain('Chargement…');
  });

  it('la page reste affichée : titre, sélecteur de mois, aucun lien ni bouton', () => {
    const el = monter({ status: 404, error: { error: SERVEUR } });

    expect(el.querySelector('page-header')).not.toBeNull();
    expect(el.querySelectorAll('select')).toHaveLength(1);
    expect(el.querySelector('a')).toBeNull();
    expect(el.querySelector('button')).toBeNull();
  });

  it('un autre 404 (route absente) n\'est pas pris pour l\'option coupée : erreur serveur, cartes conservées', () => {
    const el = monter({ status: 404, error: null });

    expect(el.querySelector('.self-notice')?.getAttribute('data-kind')).toBe('server');
    expect(el.querySelectorAll('h2')).toHaveLength(4);
  });

  it('l\'option coupée l\'emporte sur les autres erreurs reçues en même temps', () => {
    const coupee = describeSelfError({ status: 404, error: { error: SERVEUR } });

    for (const autre of [{ status: 403, error: { error: 'x : reconnectez-vous.' } }, { status: 403, error: { error: 'x' } }, { status: 500 }, { status: 0 }]) {
      expect(strongestNotice(describeSelfError(autre), coupee)).toBe(coupee);
      expect(strongestNotice(coupee, describeSelfError(autre))).toBe(coupee);
    }
  });
});
