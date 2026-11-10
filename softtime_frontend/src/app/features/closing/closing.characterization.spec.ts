import { describe, expect, it } from 'vitest';
import { ClosingReport } from '../../shared/models';
import {
  CORRECTION_TARGETS,
  SEVERITIES,
  checklistLink,
  correctionLink,
  defaultPeriod,
  exportSheets,
  statusView,
} from './closing.logic';

/**
 * Caractérisation de l'écran Contrôle avant paie, figée avant toute évolution : cibles de
 * correction (page, droit), liens de la checklist, ordre des gravités, libellés de statut et
 * structure de l'export Excel. Un ajout additif reste permis ; un renommage, un changement de
 * route, de droit ou de colonne existante casse ces tests.
 */
describe('caractérisation du contrôle avant paie', () => {
  const report: ClosingReport = {
    from: '2025-11-01T00:00:00Z',
    to: '2025-11-30T00:00:00Z',
    departement: null,
    statut: 'ACorriger',
    statutLibelle: 'À corriger',
    seuilBloquant: 0.05,
    score: 76,
    formule: 'formule',
    joursEmployeControles: 10,
    nombreAnomalies: 4,
    parGravite: [
      { cle: 'Critique', nombre: 2 },
      { cle: 'Importante', nombre: 0 },
      { cle: 'Mineure', nombre: 2 },
      { cle: 'Information', nombre: 0 },
    ],
    parType: [{ cle: 'PointageManquant', nombre: 1 }],
    anomalies: {
      items: [
        {
          type: 'PointageManquant',
          gravite: 'Critique',
          poids: 5,
          matricule: '002',
          nom: 'NOM',
          prenom: 'Prénom',
          departement: 'RH',
          date: '2025-11-17T00:00:00Z',
          detail: 'détail',
          heures: 8.5,
        },
      ],
      total: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
    },
    parDepartement: [],
    comparaison: null,
    nomsAffiches: true,
  };

  it('fige la page de correction et le droit exigé pour chaque type', () => {
    expect(CORRECTION_TARGETS['PointageIncomplet']).toMatchObject({ path: '/anomalies', permission: 'PROC:ANOMALIES', droit: 3 });
    expect(CORRECTION_TARGETS['PointageManquant']).toMatchObject({ path: '/corrected-hours', permission: 'PROC:CORRECTED_HOURS', droit: 3 });
    expect(CORRECTION_TARGETS['PlanningSansPointage']).toMatchObject({ path: '/corrected-hours', permission: 'PROC:CORRECTED_HOURS', droit: 3 });
    expect(CORRECTION_TARGETS['PointageSansPlanning']).toMatchObject({ path: '/employee-planning', permission: 'PROC:EMPLOYEE_PLANNING', droit: 3 });
    expect(CORRECTION_TARGETS['DoublonExact']).toMatchObject({ path: '/punches', permission: 'PROC:PUNCHES', droit: 3 });
    expect(CORRECTION_TARGETS['QuasiDoublon']).toMatchObject({ path: '/punches', permission: 'PROC:PUNCHES', droit: 3 });
    expect(CORRECTION_TARGETS['FerieNonApplique']).toMatchObject({ path: '/anomalies', permission: 'PROC:ANOMALIES', droit: 3 });
  });

  it('fige le lien de correction prérempli (du, au, matricule)', () => {
    const link = correctionLink({ type: 'QuasiDoublon', matricule: '001', date: '2025-11-20' });
    expect(link).toMatchObject({
      path: '/punches',
      queryParams: { du: '2025-11-20', au: '2025-11-21', matricule: '001' },
    });
    // Férié sans pointage : rien à corriger, donc aucun lien.
    expect(correctionLink({ type: 'FerieNonApplique', matricule: '001', date: '2025-11-20', pointageExiste: false })).toBeNull();
  });

  it("fige l'écran cible de chaque étape de la checklist", () => {
    const periode = { from: '2025-11-01', to: '2025-11-30' };
    expect(checklistLink({ code: 'Pointages' }, periode)).toMatchObject({ path: '/punches', permission: 'PROC:PUNCHES' });
    expect(checklistLink({ code: 'Anomalies' }, periode)).toMatchObject({ path: '/anomalies', permission: 'PROC:ANOMALIES' });
    expect(checklistLink({ code: 'Semaines' }, periode)).toMatchObject({ path: '/weekly-validation', permission: 'PROC:WEEKLY_VALIDATION' });
    expect(checklistLink({ code: 'ExoImpo' }, periode)).toMatchObject({ path: '/overtime', permission: 'PROC:OVERTIME' });
    expect(checklistLink({ code: 'HeuresImposables' }, periode)).toMatchObject({ path: '/overtime' });
    expect(checklistLink({ code: 'SynchroPaie' }, periode)).toMatchObject({ path: '/overtime' });
    expect(checklistLink({ code: 'Inconnu' }, periode)).toBeNull();
  });

  it("fige l'ordre des gravités, les libellés de statut et la période par défaut", () => {
    expect([...SEVERITIES]).toEqual(['Critique', 'Importante', 'Mineure', 'Information']);
    expect(statusView('Pret')).toMatchObject({ label: 'Prêt', tone: 'success' });
    expect(statusView('ACorriger')).toMatchObject({ label: 'À corriger', tone: 'warning' });
    expect(statusView('Bloquant')).toMatchObject({ label: 'Bloquant', tone: 'danger' });
    expect(statusView(null)).toMatchObject({ label: 'Sans statut', tone: 'muted' });
    // Période par défaut : le mois civil complet précédent, même règle que le serveur.
    expect(defaultPeriod(new Date(2025, 11, 10))).toEqual({ from: '2025-11-01', to: '2025-11-30' });
  });

  it("fige la structure actuelle de l'export Excel (synthèse + anomalies)", () => {
    const filter = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };
    const sheets = exportSheets(report, report.anomalies.items, filter, new Date(2025, 11, 1, 10, 0));

    const noms = sheets.map((s) => s.sheetName);
    expect(noms).toContain('Synthèse');
    expect(noms).toContain('Anomalies');

    const anomalies = sheets.find((s) => s.sheetName === 'Anomalies')!;
    expect(anomalies.headers).toEqual(['Gravité', 'Type', 'Matricule', 'Salarié', 'Département', 'Date', 'Détail']);
    expect(anomalies.rows[0]).toEqual(['Critique', 'Pointage manquant', '002', 'NOM Prénom', 'RH', '17/11/2025', 'détail']);

    const synthese = sheets.find((s) => s.sheetName === 'Synthèse')!;
    const libelles = synthese.rows.map((r) => r[0]);
    for (const attendu of [
      'Période', 'Département', 'Comparaison avec la période précédente', 'Analyse du', 'Statut',
      'Score', 'Jours-employé contrôlés', 'Anomalies (toutes gravités)',
      'Lignes de la feuille « Anomalies »',
    ])
      expect(libelles).toContain(attendu);
  });
});
