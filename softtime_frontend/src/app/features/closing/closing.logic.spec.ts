import { describe, expect, it } from 'vitest';
import { ClosingComparison, ClosingReport } from '../../shared/models';
import { readDeepLink } from '../../shared/utils/deep-link';
import {
  correctionLink,
  CORRECTION_TARGETS,
  EXPORT_HEADERS,
  exportFileName,
  exportRows,
  checklistLink,
  checklistStateView,
  analysisAgeText,
  resultBannerText,
  severityFilterText,
  toggleSeverity,
  sameFilter,
  dashboardCardView,
  defaultPeriod,
  excludedEmployeesText,
  formatDay,
  INFORMATION_RULE,
  isDisabledError,
  jourSuivant,
  NO_ANOMALY,
  NO_ANOMALY_TO_FIX,
  NO_CONTROLLED_DAY,
  nothingToFix,
  NOTHING_TO_FIX,
  personLabel,
  scoreText,
  SEVERITIES,
  severityLabel,
  severitySentence,
  severityTone,
  statusRuleText,
  statusView,
  summarySentences,
  typeLabel,
} from './closing.logic';

type Summary = Parameters<typeof summarySentences>[0];

const rapport = (over: Partial<Summary> = {}): Summary => ({
  score: 80,
  joursEmployeControles: 100,
  nombreAnomalies: 0,
  parType: [],
  comparaison: null,
  ...over,
});

const comparaison = (parType: ClosingComparison['parType'], ecartScore: number | null = null): ClosingComparison => ({
  from: '2025-10-01T00:00:00',
  to: '2025-10-31T00:00:00',
  scorePrecedent: 70,
  joursEmployeControlesPrecedent: 90,
  ecartScore,
  parType,
});

describe('période par défaut', () => {
  it('mois civil complet précédent, comme le serveur', () => {
    expect(defaultPeriod(new Date(2025, 11, 1))).toEqual({ from: '2025-11-01', to: '2025-11-30' });
    expect(defaultPeriod(new Date(2025, 11, 31, 23, 59))).toEqual({ from: '2025-11-01', to: '2025-11-30' });
    expect(defaultPeriod(new Date(2024, 2, 10))).toEqual({ from: '2024-02-01', to: '2024-02-29' });
  });

  it('janvier donne décembre de l’année précédente', () => {
    expect(defaultPeriod(new Date(2026, 0, 15))).toEqual({ from: '2025-12-01', to: '2025-12-31' });
  });
});

describe('statut et score', () => {
  it('chaque statut a un texte et une nuance (jamais la couleur seule)', () => {
    expect(statusView('Pret')).toMatchObject({ label: 'Prêt', tone: 'success' });
    expect(statusView('ACorriger')).toMatchObject({ label: 'À corriger', tone: 'warning' });
    expect(statusView('Bloquant')).toMatchObject({ label: 'Bloquant', tone: 'danger' });
    for (const s of ['Pret', 'ACorriger', 'Bloquant', null]) {
      const v = statusView(s);
      expect(v.label.length).toBeGreaterThan(0);
      expect(v.hint.length).toBeGreaterThan(0);
    }
  });

  it('sans statut ni score : « aucun jour contrôlé », jamais 0 ni 100', () => {
    expect(statusView(null)).toMatchObject({ label: 'Sans statut', tone: 'muted' });
    expect(scoreText(null)).toBe(NO_CONTROLLED_DAY);
    expect(scoreText(undefined)).toBe(NO_CONTROLLED_DAY);
    expect(scoreText(null)).not.toMatch(/\b(0|100)\b/);
    expect(scoreText(0)).toBe('0 / 100');
    expect(scoreText(87)).toBe('87 / 100');
  });

  it('règle du statut expliquée avec le seuil du serveur', () => {
    expect(statusRuleText(0.02)).toContain('2 %');
    expect(statusRuleText(0.025)).toContain('2,5 %');
    // Le seuil affiché est celui du serveur (défaut 5 %), jamais une valeur écrite en dur.
    expect(statusRuleText(0.05)).toContain('5 %');
    expect(statusRuleText(0.05)).not.toContain('2 %');
  });
});

describe('libellés', () => {
  it('types au singulier et au pluriel', () => {
    expect(typeLabel('PointageIncomplet')).toBe('Pointage incomplet');
    expect(typeLabel('PointageIncomplet', 12)).toBe('Pointages incomplets');
    expect(typeLabel('PlanningSansPointage', 2)).toBe('Plannings sans pointage');
    expect(typeLabel('FerieNonApplique', 3)).toBe('Fériés non appliqués');
    expect(typeLabel('QuasiDoublon', 1)).toBe('Quasi-doublon');
    expect(typeLabel('TypeInconnu')).toBe('TypeInconnu');
  });

  it('gravités et nuances', () => {
    expect(severityLabel('Critique')).toBe('Critique');
    expect(severityLabel('Importante')).toBe('Importante');
    expect(severityLabel('Mineure')).toBe('Mineure');
    expect(severityTone('Critique')).toBe('danger');
    expect(severityTone('Importante')).toBe('warning');
    expect(severityTone('Mineure')).toBe('info');
    expect(severityLabel('Information')).toBe('Information');
    expect(severityTone('Information')).toBe('muted');
    expect(SEVERITIES).toEqual(['Critique', 'Importante', 'Mineure', 'Information']);
  });

  it('phrase de la carte Information : texte, jamais alarmiste', () => {
    expect(severitySentence('Information', 0)).toBe('Aucune information');
    expect(severitySentence('Information', 1)).toBe('1 information sans effet sur le score');
    expect(severitySentence('Information', 567)).toBe('567 informations sans effet sur le score');
    expect(severitySentence('Information', 3)).not.toContain('anomalie');
  });

  it('phrases de comptage par gravité accordées', () => {
    expect(severitySentence('Critique', 0)).toBe('Aucune anomalie critique');
    expect(severitySentence('Critique', 1)).toBe('1 anomalie critique');
    expect(severitySentence('Critique', 3)).toBe('3 anomalies critiques');
    expect(severitySentence('Mineure', 2)).toBe('2 anomalies mineures');
  });

  it('salarié : nom et prénom, sinon le matricule seul', () => {
    expect(personLabel({ matricule: '777', nom: 'Rabe', prenom: 'Lova' })).toBe('Rabe Lova');
    expect(personLabel({ matricule: '777', nom: null, prenom: null })).toBe('777');
    expect(formatDay('2025-11-03T00:00:00')).toBe('03/11/2025');
    expect(formatDay(null)).toBe('—');
  });
});

describe('résumé en phrases', () => {
  it('pluriel, singulier, sans comparaison : aucune phrase de comparaison', () => {
    const phrases = summarySentences(
      rapport({
        nombreAnomalies: 13,
        parType: [
          { cle: 'PointageIncomplet', nombre: 12 },
          { cle: 'DoublonExact', nombre: 1 },
        ],
      }),
    );
    expect(phrases).toEqual(['12 pointages incomplets.', '1 doublon exact.']);
    expect(phrases.join(' ')).not.toContain('précédente');
  });

  it('dégradation et amélioration par rapport à la période précédente', () => {
    const phrases = summarySentences(
      rapport({
        nombreAnomalies: 14,
        parType: [
          { cle: 'PointageIncomplet', nombre: 12 },
          { cle: 'PointageManquant', nombre: 2 },
        ],
        comparaison: comparaison(
          [
            { type: 'PointageIncomplet', precedent: 9, actuel: 12, ecart: 3 },
            { type: 'PointageManquant', precedent: 5, actuel: 2, ecart: -3 },
            { type: 'QuasiDoublon', precedent: 4, actuel: 0, ecart: -4 },
          ],
          10,
        ),
      }),
    );
    expect(phrases).toContain('12 pointages incomplets, 3 de plus que la période précédente.');
    expect(phrases).toContain('2 pointages manquants, 3 de moins que la période précédente.');
    expect(phrases).toContain('Plus aucun quasi-doublon (4 sur la période précédente).');
    expect(phrases).toContain('Score en hausse de 10 points par rapport à la période précédente.');
  });

  it('score en baisse, stable, et un point au singulier', () => {
    const baisse = summarySentences(rapport({ comparaison: comparaison([], -1) }));
    expect(baisse).toContain('Score en baisse de 1 point par rapport à la période précédente.');
    const stable = summarySentences(rapport({ comparaison: comparaison([{ type: 'DoublonExact', precedent: 2, actuel: 2, ecart: 0 }], 0) }));
    expect(stable).toContain('Score identique à celui de la période précédente.');
  });

  it('informations : phrase calme, sans effet sur le score ni le statut', () => {
    const phrases = summarySentences(
      rapport({
        nombreAnomalies: 12,
        parType: [{ cle: 'DoublonExact', nombre: 12 }],
        parGravite: [{ cle: 'Information', nombre: 12 }],
      }),
    );
    expect(phrases).toContain(NO_ANOMALY_TO_FIX);
    expect(phrases).toContain('12 informations sans effet sur le score : elles ne comptent ni dans le score ni dans le statut.');
    expect(phrases).not.toContain(NO_ANOMALY);
  });

  it('informations en plus d’anomalies à corriger : pas de message « prêt »', () => {
    const phrases = summarySentences(
      rapport({
        nombreAnomalies: 5,
        parType: [{ cle: 'PointageManquant', nombre: 3 }],
        parGravite: [{ cle: 'Critique', nombre: 3 }, { cle: 'Information', nombre: 2 }],
      }),
    );
    expect(phrases).not.toContain(NO_ANOMALY_TO_FIX);
    expect(phrases).toContain('3 pointages manquants.');
    expect(phrases).toContain('2 informations sans effet sur le score : elles ne comptent ni dans le score ni dans le statut.');
  });

  it('salariés sans planning non contrôlés : mentionnés seulement quand il y en a', () => {
    expect(excludedEmployeesText(0)).toBe('');
    expect(excludedEmployeesText(undefined)).toBe('');
    expect(excludedEmployeesText(1)).toBe('1 salarié sans aucun planning sur la période n’est pas contrôlé.');
    expect(excludedEmployeesText(22)).toBe('22 salariés sans aucun planning sur la période ne sont pas contrôlés.');
    expect(summarySentences(rapport({ salariesSansPlanningExclus: 22 }))).toContain(excludedEmployeesText(22));
    expect(summarySentences(rapport({ salariesSansPlanningExclus: 0 })).join(' ')).not.toContain('sans aucun planning');
  });

  it('l’explication du statut indique le rôle de « Information »', () => {
    expect(INFORMATION_RULE).toContain('Information');
    expect(INFORMATION_RULE).toContain('ni dans le statut');
  });

  it('aucune anomalie : message positif ; aucun jour contrôlé : une seule phrase', () => {
    expect(summarySentences(rapport())).toEqual([NO_ANOMALY]);
    expect(summarySentences(rapport({ score: null, joursEmployeControles: 0 }))).toEqual([NO_CONTROLLED_DAY + '.']);
  });
});

describe('liens de correction', () => {
  const anomalie = { type: 'PointageIncomplet', matricule: '777', date: '2025-11-03T00:00:00' };

  it('chaque type mène à sa page, avec le droit de cette page', () => {
    expect(correctionLink(anomalie)?.path).toBe('/anomalies');
    expect(correctionLink({ ...anomalie, type: 'PointageManquant' })?.path).toBe('/corrected-hours');
    expect(correctionLink({ ...anomalie, type: 'PlanningSansPointage' })?.path).toBe('/corrected-hours');
    expect(correctionLink({ ...anomalie, type: 'PointageSansPlanning' })?.path).toBe('/employee-planning');
    expect(correctionLink({ ...anomalie, type: 'DoublonExact' })?.path).toBe('/punches');
    expect(correctionLink({ ...anomalie, type: 'QuasiDoublon' })?.path).toBe('/punches');
    expect(correctionLink({ ...anomalie, type: 'FerieNonApplique', pointageExiste: true })).toMatchObject({
      path: '/anomalies',
      permission: 'PROC:ANOMALIES',
      droit: 3,
    });
    expect(Object.keys(CORRECTION_TARGETS)).toHaveLength(9);
    expect(correctionLink({ ...anomalie, type: 'Inconnu' })).toBeNull();
  });

  it('férié non appliqué : lien vers Anomalies seulement s’il existe un pointage ce jour-là', () => {
    const ferie = { ...anomalie, type: 'FerieNonApplique' };
    expect(correctionLink({ ...ferie, pointageExiste: true })?.queryParams).toEqual({ du: '2025-11-03', au: '2025-11-04', matricule: '777' });
    // Aucun pointage, ou information absente : aucun lien (et aucune erreur).
    expect(correctionLink({ ...ferie, pointageExiste: false })).toBeNull();
    expect(correctionLink({ ...ferie, pointageExiste: null })).toBeNull();
    expect(correctionLink(ferie)).toBeNull();
    expect(nothingToFix({ ...ferie, pointageExiste: false })).toBe(true);
    expect(nothingToFix({ ...ferie, pointageExiste: true })).toBe(false);
    expect(nothingToFix({ type: 'PointageIncomplet' })).toBe(false);
    expect(NOTHING_TO_FIX).toBe('Rien à corriger dans l’application');
  });

  it('avec paramètres quand la page cible les lit', () => {
    const link = correctionLink(anomalie, new Set(['/anomalies']));
    expect(link?.queryParams).toEqual({ du: '2025-11-03', au: '2025-11-04', matricule: '777' });
    expect(correctionLink(anomalie, new Set())?.queryParams).toBeNull();
  });

  it('paramètres exacts de chaque type (du, au, matricule)', () => {
    const a = { ...anomalie, matricule: '4171', date: '2025-11-12T00:00:00' };
    const du = '2025-11-12';
    const lendemain = '2025-11-13';
    const attendu: Record<string, { path: string; au: string } | null> = {
      PointageIncomplet: { path: '/anomalies', au: lendemain },
      PointageManquant: { path: '/corrected-hours', au: du },
      PlanningSansPointage: { path: '/corrected-hours', au: du },
      PointageSansPlanning: { path: '/employee-planning', au: du },
      DoublonExact: { path: '/punches', au: lendemain },
      QuasiDoublon: { path: '/punches', au: lendemain },
      FerieNonApplique: { path: '/anomalies', au: lendemain },
    };
    for (const [type, cible] of Object.entries(attendu)) {
      const link = correctionLink({ ...a, type, pointageExiste: true });
      expect(link?.path).toBe(cible?.path);
      expect(link?.queryParams).toEqual({ du, au: cible?.au, matricule: '4171' });
    }
  });

  it('jour suivant : fin de mois, d’année et année bissextile', () => {
    expect(jourSuivant('2025-11-30')).toBe('2025-12-01');
    expect(jourSuivant('2025-12-31')).toBe('2026-01-01');
    expect(jourSuivant('2024-02-28')).toBe('2024-02-29');
    expect(jourSuivant('2025-02-28')).toBe('2025-03-01');
    expect(jourSuivant('n’importe quoi')).toBe('');
    expect(correctionLink({ ...anomalie, date: '2025-12-31T00:00:00' })?.queryParams)
      .toEqual({ du: '2025-12-31', au: '2026-01-01', matricule: '777' });
  });

  it('les paramètres produits sont acceptés par la validation des pages', () => {
    const q = correctionLink({ ...anomalie, type: 'DoublonExact', matricule: '4171', date: '2025-12-31T00:00:00' })?.queryParams;
    expect(readDeepLink({ du: q?.['du'], au: q?.['au'], matricule: q?.['matricule'] }))
      .toEqual({ from: '2025-12-31', to: '2026-01-01', matricule: '4171' });
  });

  it('libellé accessible complet pour le lecteur d’écran', () => {
    expect(correctionLink(anomalie)?.ariaLabel).toBe('Corriger dans Anomalies : pointage incomplet, matricule 777, 03/11/2025');
  });
});

describe('fonction désactivée et carte du tableau de bord', () => {
  it('reconnaît le 404 « désactivée » et pas un autre 404', () => {
    expect(isDisabledError({ status: 404, error: { error: 'La clôture de paie assistée est désactivée.' } })).toBe(true);
    expect(isDisabledError({ status: 404, error: { error: 'Introuvable.' } })).toBe(false);
    expect(isDisabledError({ status: 403, error: { error: 'désactivée' } })).toBe(false);
    expect(isDisabledError(null)).toBe(false);
  });

  it('carte : statut, score et critiques ; sans jour contrôlé, aucun score inventé', () => {
    const base = { statut: 'ACorriger', score: 91, joursEmployeControles: 200, parGravite: [{ cle: 'Critique', nombre: 2 }] } as Pick<
      ClosingReport,
      'statut' | 'score' | 'parGravite' | 'joursEmployeControles'
    >;
    expect(dashboardCardView(base)).toEqual({
      status: { label: 'À corriger', tone: 'warning' },
      score: '91 / 100',
      critiques: '2 anomalies critiques',
    });
    expect(dashboardCardView({ ...base, statut: null, score: null, joursEmployeControles: 0 })).toEqual({
      status: { label: 'Sans statut', tone: 'muted' },
      score: NO_CONTROLLED_DAY,
      critiques: '',
    });
  });
});

describe('filtre conservé : comparaison et bandeau', () => {
  const base = { from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false };

  it('sameFilter : département vide = tous, tout autre écart = filtre modifié', () => {
    expect(sameFilter(base, { ...base, departement: '' as unknown as null })).toBe(true);
    expect(sameFilter(base, { ...base, departement: 'D1' })).toBe(false);
    expect(sameFilter(base, { ...base, from: '2025-11-02' })).toBe(false);
    expect(sameFilter(base, { ...base, includeComparison: true })).toBe(false);
    expect(sameFilter(base, null)).toBe(false);
  });

  it('resultBannerText : JJ/MM à HH:mm, période et département', () => {
    const quand = new Date(2026, 0, 5, 9, 7);
    expect(resultBannerText(quand, base)).toBe('Résultat du 05/01 à 09:07, période du 01/11/2025 au 30/11/2025, département tous.');
    expect(resultBannerText(quand, { ...base, departement: 'Atelier' })).toContain('département Atelier.');
  });
});

describe('export de la liste', () => {
  const ligne = (n: number) => ({
    type: 'PointageManquant', gravite: 'Critique', poids: 5, matricule: `M${n}`, nom: null, prenom: null,
    departement: null, date: '2025-11-03T00:00:00', detail: `ligne ${n}`, heures: null, pointageExiste: null,
  });

  it('même nombre de lignes et même ordre que la liste, une cellule par en-tête, sans colonne de lien', () => {
    const items = [ligne(2), ligne(1), ligne(3)];
    const rows = exportRows(items);
    expect(rows).toHaveLength(3);
    expect(rows.map((r) => r[6])).toEqual(['ligne 2', 'ligne 1', 'ligne 3']);
    expect(rows.every((r) => r.length === EXPORT_HEADERS.length)).toBe(true);
    expect(EXPORT_HEADERS).not.toContain('Correction');
  });

  it('libellés lisibles : gravité, type, jour JJ/MM/AAAA, département vide', () => {
    const [r] = exportRows([ligne(1)]);
    expect(r[0]).toBe('Critique');
    expect(r[1]).toBe('Pointage manquant');
    expect(r[5]).toBe('03/11/2025');
    expect(r[4]).toBe('');
  });

  it('nom de fichier : période incluse', () => {
    expect(exportFileName({ from: '2025-11-01', to: '2025-11-30', departement: null, includeComparison: false }))
      .toBe('controle-avant-paie_2025-11-01_2025-11-30');
  });
});

describe('filtre de gravité : bascules', () => {
  it('toggleSeverity : ajoute et retire, toujours du plus grave au moins grave', () => {
    expect(toggleSeverity([], 'Mineure')).toEqual(['Mineure']);
    expect(toggleSeverity(['Mineure'], 'Critique')).toEqual(['Critique', 'Mineure']);
    expect(toggleSeverity(['Critique', 'Mineure'], 'Critique')).toEqual(['Mineure']);
    expect(toggleSeverity(['Mineure'], 'Mineure')).toEqual([]);
  });

  it('severityFilterText : vide sans filtre, sinon les libellés des niveaux', () => {
    expect(severityFilterText([])).toBe('');
    expect(severityFilterText(['Critique', 'Importante'])).toBe('Filtre de gravité : Critique, Importante');
  });
});

describe('checklist d’avancement : états et liens', () => {
  it('états en clair, jamais seulement une couleur', () => {
    expect(checklistStateView('Fait')).toEqual({ label: 'Fait', tone: 'success' });
    expect(checklistStateView('AFaire')).toEqual({ label: 'À faire', tone: 'warning' });
    expect(checklistStateView('Information')).toEqual({ label: 'Information', tone: 'info' });
    expect(checklistStateView('NonDeterminable')).toEqual({ label: 'Non déterminable', tone: 'muted' });
    expect(checklistStateView('autre').label).toBe('Non déterminable');
  });

  it('lien : écran existant ; période transmise seulement aux écrans qui la lisent', () => {
    const periode = { from: '2025-11-01', to: '2025-11-30' };
    expect(checklistLink({ code: 'Pointages' }, periode)).toMatchObject({ path: '/punches', queryParams: { du: '2025-11-01', au: '2025-11-30' } });
    expect(checklistLink({ code: 'Anomalies' }, periode)).toMatchObject({ path: '/anomalies', queryParams: { du: '2025-11-01', au: '2025-11-30' } });
    expect(checklistLink({ code: 'Semaines' }, periode)).toMatchObject({ path: '/weekly-validation', queryParams: null });
    expect(checklistLink({ code: 'ExoImpo' }, periode)).toMatchObject({ path: '/overtime', queryParams: null });
    expect(checklistLink({ code: 'Inconnue' }, periode)).toBeNull();
  });
});

describe('âge de l’analyse', () => {
  const t0 = new Date(2026, 0, 5, 9, 0, 0);
  const apres = (minutes: number, secondes = 0) => new Date(t0.getTime() + minutes * 60_000 + secondes * 1000);

  it('minutes, puis heures et minutes ; jamais de valeur négative', () => {
    expect(analysisAgeText(t0, apres(0, 59))).toBe('Analyse faite il y a moins d’une minute.');
    expect(analysisAgeText(t0, apres(1))).toBe('Analyse faite il y a 1 min.');
    expect(analysisAgeText(t0, apres(59, 59))).toBe('Analyse faite il y a 59 min.');
    expect(analysisAgeText(t0, apres(60))).toBe('Analyse faite il y a 1 h.');
    expect(analysisAgeText(t0, apres(125))).toBe('Analyse faite il y a 2 h 5 min.');
    expect(analysisAgeText(t0, apres(-5))).toBe('Analyse faite il y a moins d’une minute.');
  });
});
