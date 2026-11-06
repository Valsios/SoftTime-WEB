import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';

export interface SelfPeriod {
  from: string;
  to: string;
}

export interface SelfWeek {
  lundi: string;
  dimanche: string;
  heuresTravaillees: number;
  heuresSupplementaires: number;
  hs130: number;
  hs150: number;
  nuit: number;
  dimanches: number;
  feries: number;
  retard: number;
  absence: number;
}

export interface SelfExoImpo {
  periodeDebut: string;
  periodeFin: string;
  exo130: number;
  exo150: number;
  imp130: number;
  imp150: number;
}

export interface SelfWeeks {
  periode: SelfPeriod;
  semaines: SelfWeek[];
  exoImpo: SelfExoImpo[];
}

export interface SelfAnomaly {
  date: string;
  nature: string;
  heure1: string | null;
  heure2: string | null;
  ferie: boolean;
}

export interface SelfAnomalies {
  periode: SelfPeriod;
  anomalies: SelfAnomaly[];
}

export interface SelfAbsence {
  date: string;
  periode: string;
  type: string;
  dureeHeures: number | null;
  motif: string | null;
}

export interface SelfAbsences {
  periode: SelfPeriod;
  absences: SelfAbsence[];
}

export interface SelfDelay {
  date: string;
  heureEntree: string | null;
  heurePrevue: string | null;
  toleranceMinutes: number;
  minutesRetard: number;
  retardSaisi: boolean;
}

export interface SelfDelays {
  periode: SelfPeriod;
  retards: SelfDelay[];
}

/**
 * Mon espace : quatre lectures (GET api/me/space/…) filtrées par le serveur sur le matricule du jeton. Le client n'envoie que la
 * période : aucun matricule, aucun identifiant, aucun en-tête de base (un salarié n'a pas de porte, le serveur déduit sa base).
 */
@Injectable({ providedIn: 'root' })
export class SelfSpaceService extends ApiService {
  weeks(range: SelfPeriod): Observable<SelfWeeks> {
    return this.http.get<SelfWeeks>(this.url('/api/me/space/weeks'), { params: this.params({ ...range }) });
  }

  anomalies(range: SelfPeriod): Observable<SelfAnomalies> {
    return this.http.get<SelfAnomalies>(this.url('/api/me/space/anomalies'), { params: this.params({ ...range }) });
  }

  absences(range: SelfPeriod): Observable<SelfAbsences> {
    return this.http.get<SelfAbsences>(this.url('/api/me/space/absences'), { params: this.params({ ...range }) });
  }

  delays(range: SelfPeriod): Observable<SelfDelays> {
    return this.http.get<SelfDelays>(this.url('/api/me/space/delays'), { params: this.params({ ...range }) });
  }
}
