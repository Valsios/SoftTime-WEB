import { describe, expect, it } from 'vitest';
import { ANOMALY_TYPES, correctionLink, summarySentences, typeLabel } from './closing.logic';

/** Lot E : les deux signaux de vigilance (niveau Information par défaut) à l'écran. */
describe('signaux de vigilance : libellés et liens', () => {
  it('ajoutés en fin de liste : l’ordre des sept types existants ne change pas', () => {
    expect([...ANOMALY_TYPES]).toEqual([
      'PointageIncomplet', 'PointageManquant', 'PlanningSansPointage', 'PointageSansPlanning',
      'DoublonExact', 'QuasiDoublon', 'FerieNonApplique', 'CorrectionsManuelles', 'HeuresSupAtypiques',
    ]);
  });

  it('libellés singulier / pluriel', () => {
    expect(typeLabel('CorrectionsManuelles')).toBe('Correction manuelle massive');
    expect(typeLabel('CorrectionsManuelles', 2)).toBe('Corrections manuelles massives');
    expect(typeLabel('HeuresSupAtypiques')).toBe('Semaine à heures supp. atypiques');
    expect(typeLabel('HeuresSupAtypiques', 3)).toBe('Semaines à heures supp. atypiques');
  });

  it('corrections manuelles : Heures corrigées sur TOUTE la période analysée, matricule prérempli', () => {
    const link = correctionLink(
      { type: 'CorrectionsManuelles', matricule: '777', date: '2025-11-21T00:00:00' },
      undefined,
      { from: '2025-11-01T00:00:00', to: '2025-11-30T00:00:00' },
    );
    expect(link).toMatchObject({
      path: '/corrected-hours',
      permission: 'PROC:CORRECTED_HOURS',
      queryParams: { du: '2025-11-01', au: '2025-11-30', matricule: '777' },
    });
  });

  it('corrections manuelles sans période connue : repli sur le jour de l’anomalie (jamais d’erreur)', () => {
    const link = correctionLink({ type: 'CorrectionsManuelles', matricule: '777', date: '2025-11-21' });
    expect(link?.queryParams).toEqual({ du: '2025-11-21', au: '2025-11-21', matricule: '777' });
  });

  it('heures supplémentaires atypiques : Validation semaine, sans paramètre (l’écran ne lit pas du/au)', () => {
    const link = correctionLink(
      { type: 'HeuresSupAtypiques', matricule: '777', date: '2025-11-16' },
      undefined,
      { from: '2025-11-01', to: '2025-11-30' },
    );
    expect(link).toMatchObject({ path: '/weekly-validation', permission: 'PROC:WEEKLY_VALIDATION', queryParams: null });
  });

  it('les autres types gardent leur lien d’origine même quand la période est fournie', () => {
    const link = correctionLink(
      { type: 'QuasiDoublon', matricule: '001', date: '2025-11-20' },
      undefined,
      { from: '2025-11-01', to: '2025-11-30' },
    );
    expect(link?.queryParams).toEqual({ du: '2025-11-20', au: '2025-11-21', matricule: '001' });
  });

  it('résumé : une phrase par signal présent, rien quand il est absent', () => {
    const phrases = summarySentences({
      score: 97,
      joursEmployeControles: 100,
      nombreAnomalies: 3,
      parGravite: [{ cle: 'Information', nombre: 3 }],
      parType: [{ cle: 'HeuresSupAtypiques', nombre: 3 }, { cle: 'CorrectionsManuelles', nombre: 0 }],
      comparaison: null,
    });
    expect(phrases).toContain('3 semaines à heures supp. atypiques.');
    expect(phrases.join(' ')).not.toContain('correction');
  });
});
