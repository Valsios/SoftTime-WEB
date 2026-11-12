import { describe, expect, it } from 'vitest';
import { ClosingChecklist, ClosingReport } from '../../shared/models';
import { exportChecklist, exportComparison, exportDepartments, exportSheets } from './closing.logic';

/** Lot A3 : le classeur d'export porte aussi l'avancement, la comparaison et les départements affichés. */

const rapport = (over: Partial<ClosingReport> = {}): ClosingReport => ({
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  statut: 'ACorriger',
  statutLibelle: 'À corriger',
  seuilBloquant: 0.05,
  score: 97,
  formule: 'formule',
  joursEmployeControles: 100,
  nombreAnomalies: 0,
  parGravite: [],
  parType: [],
  anomalies: { items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 },
  parDepartement: [],
  comparaison: null,
  nomsAffiches: true,
  ...over,
});

const checklist: ClosingChecklist = {
  from: '2025-11-01T00:00:00',
  to: '2025-11-30T00:00:00',
  departement: null,
  lignes: [
    { code: 'Pointages', libelle: 'Pointages importés', etat: 'Fait', detail: '12 pointage(s).', nombre: 12 },
    { code: 'Semaines', libelle: 'Semaines validées', etat: 'AFaire', detail: '2 à valider.', nombre: 2 },
  ],
};

const filter = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };

describe('export Excel enrichi (avancement, comparaison, départements)', () => {
  it('sans comparaison, sans checklist, sans département : les deux feuilles historiques seulement', () => {
    const sheets = exportSheets(rapport(), [], filter, new Date(2025, 10, 1));
    expect(sheets.map((s) => s.sheetName)).toEqual(['Synthèse', 'Anomalies']);
  });

  it("la checklist chargée devient une feuille « Avancement » (états en toutes lettres, jamais de matricule)", () => {
    const sheet = exportChecklist(checklist)!;
    expect(sheet.sheetName).toBe('Avancement');
    expect(sheet.headers).toEqual(['Étape', 'État', 'Détail', 'Nombre']);
    expect(sheet.rows).toEqual([
      ['Pointages importés', 'Fait', '12 pointage(s).', 12],
      ['Semaines validées', 'À faire', '2 à valider.', 2],
    ]);
    expect(exportChecklist(null)).toBeNull();
  });

  it('la comparaison devient une feuille « Comparaison » avec le score et les écarts par type', () => {
    const sheet = exportComparison(rapport({
      comparaison: {
        from: '2025-10-01T00:00:00',
        to: '2025-10-31T00:00:00',
        scorePrecedent: 90,
        joursEmployeControlesPrecedent: 95,
        ecartScore: 7,
        parType: [{ type: 'QuasiDoublon', precedent: 4, actuel: 1, ecart: -3 }],
      },
    }))!;
    expect(sheet.sheetName).toBe('Comparaison');
    expect(sheet.rows).toContainEqual(['Score', 90, 97, 7]);
    expect(sheet.rows).toContainEqual(['Quasi-doublons', 4, 1, -3]);
    expect(sheet.rows[0][0]).toContain('du 01/10/2025 au 31/10/2025');
    expect(exportComparison(rapport())).toBeNull();
  });

  it('les départements affichés deviennent une feuille « Par département »', () => {
    const sheet = exportDepartments(rapport({
      parDepartement: [
        { departement: 'ATELIER', score: 72, joursEmployeControles: 5, nombreAnomalies: 3, parGravite: [{ cle: 'Critique', nombre: 1 }], parType: [] },
        { departement: null, score: null, joursEmployeControles: 0, nombreAnomalies: 0, parGravite: [], parType: [] },
      ],
    }))!;
    expect(sheet.rows).toEqual([
      ['ATELIER', 72, 5, 3, 1],
      ['Sans département', 'Aucun jour contrôlé', 0, 0, 0],
    ]);
    expect(exportDepartments(rapport())).toBeNull();
  });

  it('le classeur complet ordonne : Synthèse, Anomalies, Avancement, Comparaison, Par département', () => {
    const complet = rapport({
      comparaison: {
        from: '2025-10-01T00:00:00', to: '2025-10-31T00:00:00',
        scorePrecedent: 90, joursEmployeControlesPrecedent: 95, ecartScore: 7, parType: [],
      },
      parDepartement: [{ departement: 'RH', score: 80, joursEmployeControles: 5, nombreAnomalies: 1, parGravite: [], parType: [] }],
    });
    const sheets = exportSheets(complet, [], filter, new Date(2025, 10, 1), undefined, checklist);
    expect(sheets.map((s) => s.sheetName)).toEqual(['Synthèse', 'Anomalies', 'Avancement', 'Comparaison', 'Par département']);
  });
});
