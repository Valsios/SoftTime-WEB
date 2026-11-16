import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { silentContext } from '../../core/silent-request';
import {
  PayrollVariableSave,
  PayrollVariablesActive,
  PayrollVariablesConfig,
} from '../models';

/** Variables envoyées à la paie de la base active (api/v1/payroll-variables). */
@Injectable({ providedIn: 'root' })
export class PayrollVariablesService extends ApiService {
  /** Configuration complète (porte Bases + droit de modifier la base). */
  get(): Observable<PayrollVariablesConfig> {
    return this.http.get<PayrollVariablesConfig>(this.url('/api/v1/payroll-variables'));
  }

  /** Enregistre les neuf variables ; refusé (409) si le mois en cours a déjà été envoyé en paie. */
  save(variables: PayrollVariableSave[]): Observable<PayrollVariablesConfig> {
    return this.http.put<PayrollVariablesConfig>(this.url('/api/v1/payroll-variables'), variables);
  }

  /** Noms des variables actives (silencieux : un échec laisse la page Heures supplémentaires inchangée). */
  actives(): Observable<PayrollVariablesActive> {
    return this.http.get<PayrollVariablesActive>(this.url('/api/v1/payroll-variables/actives'), {
      context: silentContext(),
    });
  }
}
