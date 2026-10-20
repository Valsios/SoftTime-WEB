import { Injectable } from '@angular/core';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';
import { ApiService } from '../../core/api';
import { PasswordPolicy } from '../models';
import { FALLBACK_PASSWORD_POLICY } from '../utils/password-policy';

/**
 * Charge (une seule fois) la politique de mot de passe du serveur et la met en cache.
 * En cas d'échec réseau, la politique de repli locale est utilisée : le serveur reste
 * de toute façon l'autorité finale au moment de l'enregistrement.
 */
@Injectable({ providedIn: 'root' })
export class PasswordPolicyService extends ApiService {
  private cached$?: Observable<PasswordPolicy>;

  get(): Observable<PasswordPolicy> {
    this.cached$ ??= this.http.get<PasswordPolicy>(this.url('/api/auth/password-policy')).pipe(
      map((policy) => this.withFallback(policy)),
      catchError(() => of(FALLBACK_PASSWORD_POLICY)),
      shareReplay(1),
    );
    return this.cached$;
  }

  /** Tolère une réponse partielle (version serveur plus ancienne) sans casser l'écran. */
  private withFallback(policy: Partial<PasswordPolicy> | null): PasswordPolicy {
    return {
      minLength: policy?.minLength ?? FALLBACK_PASSWORD_POLICY.minLength,
      rules: policy?.rules?.length ? policy.rules : FALLBACK_PASSWORD_POLICY.rules,
    };
  }
}
