import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '../../core/toast.service';
import { AdminSetupRequest, PasswordPolicy } from '../../shared/models';
import { AuthService } from '../../shared/services/auth.service';
import { PasswordPolicyService } from '../../shared/services/password-policy.service';
import { PasswordPolicyHint } from '../../shared/components/password-policy-hint';
import { SoftButton } from '../../shared/components/soft-button';
import { SoftInput } from '../../shared/components/soft-input';
import {
  FALLBACK_PASSWORD_POLICY,
  evaluatePasswordPolicy,
  isPasswordCompliant,
} from '../../shared/utils/password-policy';

/**
 * Écran de démarrage initial : crée le premier compte administrateur d'une installation vierge.
 * Accessible sans authentification ; si un compte existe déjà, l'écran renvoie vers la connexion
 * (le serveur refuse de toute façon une seconde création).
 */
@Component({
  selector: 'app-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftButton, SoftInput, PasswordPolicyHint],
  template: `
    <div class="setup">
      <div class="setup__panel soft-fade-in">
        <div class="setup__brand">
          <img class="setup__logo" src="assets/SoftTimeLogo.png" alt="Soft Time" />
          <h1>Soft Time</h1>
          <p>Création du compte administrateur</p>
        </div>

        @if (creating()) {
          <p class="setup__hint">Création en cours…</p>
        } @else {
          <form (ngSubmit)="submit()">
            <soft-input label="Nom" placeholder="Nom de l'administrateur" [(ngModel)]="form.nom" name="nom" [required]="true"></soft-input>
            <soft-input label="Identifiant" placeholder="Identifiant de connexion" [(ngModel)]="form.login" name="login" [required]="true"></soft-input>
            <div class="setup__password">
              <soft-input label="Mot de passe" type="password" placeholder="••••••••" [(ngModel)]="form.password" name="password" [required]="true"></soft-input>
              <password-policy-hint
                [password]="form.password"
                [policy]="policy()"
              ></password-policy-hint>
            </div>
            <soft-input label="E-mail (facultatif)" type="email" placeholder="admin@exemple.com" [(ngModel)]="form.email" name="email"></soft-input>

            <soft-button
              type="submit"
              [fullWidth]="true"
              [loading]="creating()"
              [disabled]="!form.nom || !form.login || !form.password || !passwordValid()"
            >
              Créer l'administrateur
            </soft-button>
          </form>

          <p class="setup__hint">
            L'e-mail des identifiants n'est envoyé que si la configuration SMTP est renseignée ;
            sinon les identifiants sont à communiquer directement.
          </p>
        }
      </div>
    </div>
  `,
  styles: [
    `
      .setup {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: 24px;
        background: var(--app-bg);
      }
      .setup__panel {
        width: 100%;
        max-width: 440px;
        background: var(--app-surface);
        border: 1px solid var(--app-border);
        border-radius: var(--radius-xl);
        box-shadow: var(--shadow-xl);
        padding: 36px 32px;
      }
      .setup__brand {
        text-align: center;
        margin-bottom: 24px;
      }
      .setup__logo {
        width: 76px;
        height: 76px;
        object-fit: contain;
      }
      .setup__brand h1 {
        margin: 14px 0 4px;
        font-family: var(--font-heading);
        font-size: 24px;
        font-weight: 700;
        color: var(--navy-950);
        letter-spacing: -0.02em;
      }
      .setup__brand p {
        margin: 0;
        color: var(--text-secondary);
        font-size: 13.5px;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .setup__password {
        display: grid;
        gap: 8px;
      }
      .setup__hint {
        margin: 18px 0 0;
        color: var(--text-muted);
        font-size: 12.5px;
        text-align: center;
        line-height: 1.4;
      }
    `,
  ],
})
export class SetupPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly passwordPolicies = inject(PasswordPolicyService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly creating = signal(false);
  readonly policy = signal<PasswordPolicy | null>(null);
  form: AdminSetupRequest = { nom: '', login: '', password: '', email: '' };

  ngOnInit(): void {
    this.passwordPolicies.get().subscribe((policy) => this.policy.set(policy));

    // Un administrateur existe déjà : l'écran n'a plus lieu d'être.
    this.auth.isFirstRun().subscribe({
      next: (firstRun) => {
        if (!firstRun) {
          this.toast.warning('Un compte existe déjà : connectez-vous.');
          this.router.navigateByUrl('/login', { replaceUrl: true });
        }
      },
      error: () => undefined,
    });
  }

  passwordValid(): boolean {
    return isPasswordCompliant(
      evaluatePasswordPolicy(this.form.password ?? '', this.policy() ?? FALLBACK_PASSWORD_POLICY),
    );
  }

  submit(): void {
    if (this.creating()) return;
    if (!this.passwordValid()) {
      this.toast.warning('Choisissez un mot de passe conforme aux règles de sécurité.');
      return;
    }
    this.creating.set(true);
    this.auth.createAdmin({
      nom: this.form.nom,
      login: this.form.login,
      password: this.form.password,
      email: this.form.email || null,
    }).subscribe({
      next: (result) => {
        this.creating.set(false);
        this.toast.success(result.message);
        this.router.navigateByUrl('/login', { replaceUrl: true });
      },
      error: () => this.creating.set(false),
    });
  }
}
