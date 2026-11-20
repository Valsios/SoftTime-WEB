import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { silentContext } from '../../core/silent-request';
import { PayrollClosure, PayrollClosureState, PayrollPeriods } from '../models';

/**
 * Clôture de paie de la base active (api/v1/payroll-closures). Une seule écriture : clôturer, définitivement. Il n'existe
 * aucune méthode d'annulation ni de déblocage, ni côté serveur ni ici.
 */
@Injectable({ providedIn: 'root' })
export class PayrollClosureService extends ApiService {
  /** Périodes envoyées avec leur état (porte Bases + droit de modifier ou de clôturer la base). */
  periods(): Observable<PayrollPeriods> {
    return this.http.get<PayrollPeriods>(this.url('/api/v1/payroll-closures/periods'));
  }

  /** Clôture la période exacte d'un envoi ; un refus (409) garde son message, affiché tel quel. */
  close(from: string, to: string): Observable<PayrollClosure> {
    return this.http.post<PayrollClosure>(this.url('/api/v1/payroll-closures'), { from, to });
  }

  /** Périodes clôturées qui recoupent une sélection (silencieux : un échec n'affiche simplement pas de bandeau). */
  state(from: string, to: string): Observable<PayrollClosureState> {
    return this.http.get<PayrollClosureState>(this.url('/api/v1/payroll-closures/state'), {
      params: new HttpParams().set('from', from).set('to', to),
      context: silentContext(),
    });
  }
}
