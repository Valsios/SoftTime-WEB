import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { SelfSpacePage } from './self-space';
import { SelfSpaceService, SelfWeeks } from './self-space.service';
import { STRADDLE_NOTE } from './self-space.logic';

const periode = { from: '2026-10-01', to: '2026-10-31' };
const semaine = { lundi: '2026-09-28T00:00:00', dimanche: '2026-10-04T00:00:00', heuresTravaillees: 45, heuresSupplementaires: 10, hs130: 8, hs150: 2, nuit: 0, dimanches: 0, feries: 0, retard: 0, absence: 0 };

function monter(weeks: SelfWeeks) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: SelfSpaceService,
        useValue: {
          weeks: () => of(weeks),
          anomalies: () => of({ periode, anomalies: [] }),
          absences: () => of({ periode, absences: [] }),
          delays: () => of({ periode, retards: [] }),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(SelfSpacePage);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Mon espace : note de la semaine à cheval', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('le texte de la note dit qu\'une semaine à cheval apparaît dans chaque mois et que les totaux ne s\'additionnent pas', () => {
    expect(STRADDLE_NOTE).toBe('Une semaine à cheval sur deux mois apparaît dans chacun de ces mois : les totaux de mois différents ne s’additionnent donc pas.');
  });

  it('la note est affichée sous la carte « Mes heures par semaine », avant la carte des anomalies', () => {
    const el = monter({ periode, semaines: [semaine], exoImpo: [] });
    const note = el.querySelector('.self-note');

    expect(note?.textContent?.trim()).toBe(STRADDLE_NOTE);
    const cartes = Array.from(el.querySelectorAll('soft-card'));
    expect(cartes).toHaveLength(4);
    expect(cartes[0].compareDocumentPosition(note!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(note!.compareDocumentPosition(cartes[1]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(cartes[0].contains(note)).toBe(false);
  });

  it('le calcul n\'est pas touché : la semaine à cheval reste affichée telle que le serveur la renvoie', () => {
    const el = monter({ periode, semaines: [semaine], exoImpo: [] });

    expect(el.textContent).toContain('28/09/2026 au 04/10/2026');
    expect(el.textContent).toContain('45 h');
    expect(el.textContent).toContain('10 h');
  });

  it('sans semaine à afficher, la note n\'est pas affichée', () => {
    const el = monter({ periode, semaines: [], exoImpo: [] });

    expect(el.querySelector('.self-note')).toBeNull();
  });
});
