import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { AccessContext, gateAllows } from '../core/access.logic';
import { ConfirmService } from '../core/confirm.service';
import { SessionStore } from '../core/session.store';
import { ToastService } from '../core/toast.service';
import { HolidaysService } from '../shared/services/catalog.service';
import { ExcelExportService } from '../shared/services/excel-export.service';
import { HistoryService } from '../shared/services/reports.service';
import { HolidaysPage } from './params/holidays';
import { HistoryPage } from './reports/history';

/**
 * Un bouton visible = une action que le serveur autorise (lots 4b et 7) : rendu réel des pages, droits simulés.
 * « Importer SAGE » exige PARAMS:HOLIDAYS:CREATE ; suppression et purge de l'Historique : administrateur seulement.
 */

const emptyPage = { items: [], total: 0, page: 1, pageSize: 50, totalPages: 1 };
const toast = { error: () => undefined, success: () => undefined, warning: () => undefined, info: () => undefined };

function renderedText(component: unknown): string {
  const fixture = TestBed.createComponent(component as never);
  fixture.detectChanges();
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('Visibilité des boutons selon les droits', () => {
  afterEach(() => TestBed.resetTestingModule());

  function holidays(granted: string[]): string {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: HolidaysService, useValue: { listPaged: () => of(emptyPage) } },
        { provide: SessionStore, useValue: { hasPermission: (code?: string) => !!code && granted.includes(code) } },
        { provide: ToastService, useValue: toast },
        { provide: ConfirmService, useValue: { ask: async () => true } },
      ],
    });
    return renderedText(HolidaysPage);
  }

  it('Jours fériés : « Importer SAGE » et « Ajouter » masqués sans PARAMS:HOLIDAYS:CREATE', () => {
    const text = holidays(['PARAMS:HOLIDAYS', 'PARAMS:HOLIDAYS:UPDATE']);
    expect(text).not.toContain('Importer SAGE');
    expect(text).not.toContain('Ajouter');
  });

  it('Jours fériés : « Importer SAGE » visible avec PARAMS:HOLIDAYS:CREATE', () => {
    const text = holidays(['PARAMS:HOLIDAYS', 'PARAMS:HOLIDAYS:CREATE']);
    expect(text).toContain('Importer SAGE');
    expect(text).toContain('Ajouter');
  });

  function history(roleId: number): string {
    const context: AccessContext = { roleId, permissions: ['TRACE:AUDIT'], customized: false, gates: [5], fallbackRights: [5] };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: HistoryService,
          useValue: { pointagesPaged: () => of(emptyPage), anomaliesPaged: () => of(emptyPage), hsPaged: () => of(emptyPage) },
        },
        { provide: ExcelExportService, useValue: {} },
        { provide: SessionStore, useValue: { accessContext: () => context, hasRight: (d: number) => gateAllows(context, d) } },
        { provide: ToastService, useValue: toast },
        { provide: ConfirmService, useValue: { ask: async () => true } },
      ],
    });
    return renderedText(HistoryPage);
  }

  it('Historique : « Purger l’archive » masqué pour un compte non administrateur avec TRACE:AUDIT', () => {
    expect(history(91)).not.toContain("Purger l'archive");
  });

  it('Historique : « Purger l’archive » visible pour l’administrateur', () => {
    expect(history(1)).toContain("Purger l'archive");
  });
});
