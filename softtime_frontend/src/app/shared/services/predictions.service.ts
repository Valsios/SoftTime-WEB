import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { WeekLanding, WeekLandingWeek } from '../models';

/** Prévisions d'heures supplémentaires : atterrissage de la semaine, calculé à la demande. */
@Injectable({ providedIn: 'root' })
export class PredictionsService extends ApiService {
  /** Atterrissage par salarié de la semaine contenant `week` (semaine en cours par défaut). */
  landing(week?: string): Observable<WeekLanding> {
    return this.http.get<WeekLanding>(this.url('/api/v1/predictions/landing'), {
      params: this.params({ week }),
    });
  }

  /** Totaux d'atterrissage de la semaine en cours et des semaines suivantes. */
  horizon(): Observable<WeekLandingWeek[]> {
    return this.http.get<WeekLandingWeek[]>(this.url('/api/v1/predictions/landing/horizon'));
  }
}
