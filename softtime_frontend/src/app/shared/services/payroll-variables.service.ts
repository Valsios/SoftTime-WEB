import { HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { silentContext } from '../../core/silent-request';
import {
  PayrollTargetColumn,
  PayrollVariableSave,
  PayrollVariablesActive,
  PayrollVariablesConfig,
  PayrollWriteTargets,
  PayrollWriteTargetsSave,
} from '../models';

/** Variables envoyées à la paie de la base active (api/v1/payroll-variables). */
@Injectable({ providedIn: 'root' })
export class PayrollVariablesService extends ApiService {
  /** Configuration complète (porte Bases + droit de modifier la base). */
  get(): Observable<PayrollVariablesConfig> {
    return this.http.get<PayrollVariablesConfig>(this.url('/api/v1/payroll-variables'));
  }

  /**
   * Enregistre les neuf variables et l'option « remettre à 0 les salariés sans valeur à l'envoi » en un seul appel ;
   * refusé (409) si le mois en cours a déjà été envoyé en paie.
   */
  save(variables: PayrollVariableSave[], remiseAZeroSansValeur?: boolean): Observable<PayrollVariablesConfig> {
    return this.http.put<PayrollVariablesConfig>(this.url('/api/v1/payroll-variables'), { variables, remiseAZeroSansValeur });
  }

  /** Noms des variables envoyées (silencieux : un échec laisse la page Heures supplémentaires inchangée). */
  actives(): Observable<PayrollVariablesActive> {
    return this.http.get<PayrollVariablesActive>(this.url('/api/v1/payroll-variables/actives'), {
      context: silentContext(),
    });
  }

  /** Autre base : destinations des variables (T_PAIE_ECRITURE), tables et colonnes de la base de paie. */
  destinations(): Observable<PayrollWriteTargets> {
    return this.http.get<PayrollWriteTargets>(this.url('/api/v1/payroll-variables/destinations'));
  }

  /** Autre base : colonnes d'une table de la base de paie, avec leur type. */
  colonnes(table: string): Observable<PayrollTargetColumn[]> {
    return this.http.get<PayrollTargetColumn[]>(this.url('/api/v1/payroll-variables/destinations/colonnes'), {
      params: new HttpParams().set('table', table),
    });
  }

  /** Autre base : enregistre les destinations ; refusé (409) si le mois en cours a déjà été envoyé en paie. */
  saveDestinations(destinations: PayrollWriteTargetsSave): Observable<PayrollWriteTargets> {
    return this.http.put<PayrollWriteTargets>(this.url('/api/v1/payroll-variables/destinations'), destinations);
  }
}
