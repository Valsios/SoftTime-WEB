import { describe, expect, it } from 'vitest';
import { JournalEntry } from '../../shared/models';
import {
  buildCsv,
  buildDetailRows,
  buildQueryParams,
  columnSummary,
  csvCell,
  csvLine,
  CSV_HEADERS,
  defaultFilters,
  displayValue,
  EXPORT_LIMIT_MESSAGE,
  exportFileName,
  formatLocalDateTime,
  groupOperations,
  isGuid,
  JournalFilters,
  localDayEndToUtcIso,
  localDayStartToUtcIso,
  normalizeFilters,
  parseUtcDate,
  planExport,
  sameOperation,
  totalPages,
  truncateValue,
  validateFilters,
} from './journal.logic';

function entry(partial: Partial<JournalEntry> = {}): JournalEntry {
  return {
    id: 1,
    dateUtc: '2026-10-28T12:11:53.2736559',
    table: 'T_FERIE',
    key: 'ID=1',
    column: 'LIBELLE',
    action: 'Modification',
    before: 'a',
    after: 'b',
    user: 'admin',
    sageDatabase: null,
    pointeuseDatabase: null,
    correlation: '11111111-1111-1111-1111-111111111111',
    source: 'EF:SoftTimeDbContext',
    ...partial,
  };
}

const empty: JournalFilters = normalizeFilters({});

describe('journal.logic — paramètres de requête', () => {
  it('ignore les valeurs vides et ne garde que la pagination', () => {
    expect(buildQueryParams(empty, 1, 50)).toEqual({ page: 1, pageSize: 50 });
  });

  it('retire les espaces et transmet les filtres renseignés', () => {
    const params = buildQueryParams(
      { ...empty, user: ' admin ', action: 'Insertion', table: 'T_FERIE', key: 'ID=3', sageDatabase: 'SFOI', correlation: '11111111-1111-1111-1111-111111111111' },
      2,
      25,
    );
    expect(params).toMatchObject({ user: 'admin', action: 'Insertion', table: 'T_FERIE', key: 'ID=3', sageDatabase: 'SFOI', page: 2, pageSize: 25 });
  });

  it('convertit les dates locales en instants UTC ISO avec Z (début et fin de journée)', () => {
    const params = buildQueryParams({ ...empty, from: '2026-03-10', to: '2026-03-12' }, 1, 50);
    expect(params.from).toBe(new Date(2026, 2, 10, 0, 0, 0, 0).toISOString());
    expect(params.to).toBe(new Date(2026, 2, 12, 23, 59, 59, 999).toISOString());
    expect(params.from).toMatch(/Z$/);
    expect(params.to).toMatch(/Z$/);
  });

  it('une date invalide ou vide est ignorée', () => {
    expect(localDayStartToUtcIso('')).toBeNull();
    expect(localDayEndToUtcIso('12/03/2026')).toBeNull();
    expect(buildQueryParams({ ...empty, from: 'abc' }, 1, 50).from).toBeUndefined();
  });

  it('borne la pagination : page ≥ 1, taille entre 1 et 200', () => {
    expect(buildQueryParams(empty, 0, 50).page).toBe(1);
    expect(buildQueryParams(empty, 3, 5000).pageSize).toBe(200);
    expect(buildQueryParams(empty, 3, 0).pageSize).toBe(50);
  });

  it('période par défaut : les 7 derniers jours en heure locale', () => {
    const f = defaultFilters(new Date(2026, 10, 1, 15, 30));
    expect(f.from).toBe('2026-10-25');
    expect(f.to).toBe('2026-11-01');
    expect(f.user).toBe('');
  });

  it('normalizeFilters transforme les null des listes déroulantes en chaînes vides', () => {
    const f = normalizeFilters({ user: null, action: undefined, table: ' T_FERIE ' });
    expect(f.user).toBe('');
    expect(f.action).toBe('');
    expect(f.table).toBe('T_FERIE');
  });

  it('validateFilters refuse une corrélation non GUID et une période inversée', () => {
    expect(validateFilters({ ...empty, correlation: 'abc' })).toContain('GUID');
    expect(validateFilters({ ...empty, from: '2026-03-12', to: '2026-03-10' })).toContain('début');
    expect(validateFilters({ ...empty, from: '2026-03-10', to: '2026-03-10' })).toBeNull();
    expect(isGuid('11111111-1111-1111-1111-111111111111')).toBe(true);
  });

  it('totalPages ne descend jamais sous 1', () => {
    expect(totalPages(0, 50)).toBe(1);
    expect(totalPages(101, 50)).toBe(3);
  });
});

describe('journal.logic — dates du serveur', () => {
  it('une date UTC sans Z est lue comme UTC, pas comme heure locale', () => {
    expect(parseUtcDate('2026-10-28T12:11:53.2736559').toISOString()).toBe('2026-10-28T12:11:53.273Z');
    expect(parseUtcDate('2026-10-28T12:11:53Z').toISOString()).toBe('2026-10-28T12:11:53.000Z');
    expect(parseUtcDate('2026-10-28T14:11:53+02:00').toISOString()).toBe('2026-10-28T12:11:53.000Z');
  });

  it('l’affichage suit le fuseau local', () => {
    const d = new Date('2026-10-28T12:11:53Z');
    const pad = (n: number) => String(n).padStart(2, '0');
    expect(formatLocalDateTime('2026-10-28T12:11:53')).toBe(
      `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`,
    );
    expect(formatLocalDateTime(null)).toBe('—');
  });
});

describe('journal.logic — regroupement d’une opération', () => {
  const a = '11111111-1111-1111-1111-111111111111';
  const b = '22222222-2222-2222-2222-222222222222';

  it('regroupe par corrélation, table et clé en gardant l’ordre', () => {
    const rows = [
      entry({ id: 1, correlation: a, table: 'T_FERIE', key: 'ID=1', column: 'LIBELLE' }),
      entry({ id: 2, correlation: a, table: 'T_FERIE', key: 'ID=2', column: 'LIBELLE' }),
      entry({ id: 3, correlation: a, table: 'T_FERIE', key: 'ID=1', column: 'DATE' }),
      entry({ id: 4, correlation: b, table: 'T_FERIE', key: 'ID=1', column: 'DATE' }),
      entry({ id: 5, correlation: a, table: 'T_ROLE', key: 'ID=1', column: 'NOM' }),
    ];
    const groups = groupOperations(rows);
    expect(groups.map((g) => g.rows.map((r) => r.id))).toEqual([[1, 3], [2], [4], [5]]);
  });

  it('une clé absente forme son propre groupe', () => {
    const groups = groupOperations([entry({ id: 1, key: null }), entry({ id: 2, key: null }), entry({ id: 3, key: 'ID=1' })]);
    expect(groups.map((g) => g.rows.length)).toEqual([2, 1]);
  });

  it('sameOperation compare corrélation, table et clé', () => {
    expect(sameOperation(entry(), entry({ id: 2, column: 'AUTRE' }))).toBe(true);
    expect(sameOperation(entry(), entry({ key: 'ID=2' }))).toBe(false);
    expect(sameOperation(entry({ key: null }), entry({ key: null }))).toBe(true);
    expect(sameOperation(entry(), entry({ table: 'T_ROLE' }))).toBe(false);
  });

  it('le détail Colonne | Avant | Après suit l’ordre d’écriture', () => {
    const rows = [entry({ id: 9, column: 'B', before: '1', after: '2' }), entry({ id: 3, column: 'A', before: null, after: 'x' })];
    const detail = buildDetailRows(rows);
    expect(detail.map((d) => d.column)).toEqual(['A', 'B']);
    expect(detail[0].before.kind).toBe('empty');
    expect(detail[0].after.text).toBe('x');
  });

  it('une ligne sans colonne devient « Opération entière »', () => {
    expect(buildDetailRows([entry({ column: null })])[0].column).toBe('Opération entière');
    expect(columnSummary(entry({ column: null, after: 'Synthèse volume : Insertion=300' }))).toContain('Synthèse');
    expect(columnSummary(entry({ column: null, before: null, after: null }))).toBe('—');
  });
});

describe('journal.logic — valeurs affichées', () => {
  it('une valeur masquée garde ******** et porte la mention « masqué »', () => {
    expect(displayValue('********')).toEqual({ kind: 'masked', text: '********', note: 'masqué' });
  });

  it('une valeur vide ou absente s’affiche « — »', () => {
    expect(displayValue(null).text).toBe('—');
    expect(displayValue(undefined).kind).toBe('empty');
    expect(displayValue('').kind).toBe('empty');
  });

  it('une valeur ordinaire reste telle quelle (y compris « 0 » et « non »)', () => {
    expect(displayValue('0')).toEqual({ kind: 'value', text: '0', note: null });
  });

  it('tronque un texte long et signale la troncature', () => {
    const long = 'x'.repeat(200);
    const cut = truncateValue(long);
    expect(cut.truncated).toBe(true);
    expect(cut.text.length).toBe(81);
    expect(truncateValue('court')).toEqual({ text: 'court', truncated: false });
  });
});

describe('journal.logic — export CSV', () => {
  it('préfixe d’une apostrophe les cellules qui commencent par = + - @ tabulation ou retour chariot', () => {
    expect(csvCell('=SOMME(A1:A2)')).toBe("'=SOMME(A1:A2)");
    expect(csvCell('+33612345678')).toBe("'+33612345678");
    expect(csvCell('-5')).toBe("'-5");
    expect(csvCell('@cmd')).toBe("'@cmd");
    expect(csvCell('\tabc')).toBe("'\tabc");
    expect(csvCell('\rabc')).toBe('"\'\rabc"');
  });

  it('ne modifie pas une cellule qui contient seulement ces signes ailleurs', () => {
    expect(csvCell('a=b')).toBe('a=b');
    expect(csvCell('x-y@z')).toBe('x-y@z');
  });

  it('entoure de guillemets et double les guillemets, avec séparateur ou saut de ligne', () => {
    expect(csvCell('a;b')).toBe('"a;b"');
    expect(csvCell('il dit "oui"')).toBe('"il dit ""oui"""');
    expect(csvCell('ligne1\nligne2')).toBe('"ligne1\nligne2"');
    expect(csvCell('ligne1\r\nligne2')).toBe('"ligne1\r\nligne2"');
  });

  it('protège aussi une formule qui contient des guillemets ou des points-virgules', () => {
    expect(csvCell('=A1;"x"')).toBe('"\'=A1;""x"""');
  });

  it('null et undefined donnent une cellule vide', () => {
    expect(csvCell(null)).toBe('');
    expect(csvCell(undefined)).toBe('');
    expect(csvLine(['a', null, 'b'])).toBe('a;;b');
  });

  it('le fichier a des en-têtes français, un séparateur point-virgule et des fins de ligne CRLF', () => {
    const csv = buildCsv([entry({ after: '=1+1', user: 'a;b' })]);
    const lines = csv.split('\r\n');
    expect(lines[0]).toBe(CSV_HEADERS.join(';'));
    expect(lines[0]).toContain('Utilisateur');
    expect(lines[1]).toContain("'=1+1");
    expect(lines[1]).toContain('"a;b"');
    expect(lines[2]).toBe('');
  });

  it('les valeurs masquées sont exportées telles quelles', () => {
    expect(buildCsv([entry({ column: 'PASSWORD', before: '********', after: '********' })])).toContain('********');
  });

  it('limite l’export à 5000 lignes et prévient quand le total est supérieur', () => {
    expect(planExport(0)).toEqual({ rows: 0, blocks: 0, limited: false });
    expect(planExport(450)).toEqual({ rows: 450, blocks: 3, limited: false });
    expect(planExport(5000)).toEqual({ rows: 5000, blocks: 25, limited: false });
    expect(planExport(5001)).toEqual({ rows: 5000, blocks: 25, limited: true });
    expect(EXPORT_LIMIT_MESSAGE).toBe('Export limité à 5000 lignes. Affinez les filtres.');
  });

  it('nomme le fichier avec la date locale', () => {
    expect(exportFileName(new Date(2026, 10, 1, 9, 5))).toBe('journal-audit-20261101-0905.csv');
  });
});
