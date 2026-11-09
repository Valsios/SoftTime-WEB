import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../shared/services/auth.service';
import { SessionStore } from '../../core/session.store';
import { SoftButton } from '../../shared/components/soft-button';
import { SoftInput } from '../../shared/components/soft-input';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftButton, SoftInput],
  template: `
    <div class="login">
      <div class="login__form-col">
        <div class="login__form-wrap soft-fade-in">
          <div class="login__brand">
            <img class="login__logo" src="assets/SoftTimeLogo.png" alt="Soft Time" />
          </div>
          <p class="soft-eyebrow">Console de gestion</p>
          <h1>Connexion à Soft Time</h1>
          <p class="login__help">Saisissez vos identifiants professionnels.</p>

          <form (ngSubmit)="submit()">
            <soft-input
              label="Identifiant"
              placeholder="Votre login"
              [(ngModel)]="login"
              name="login"
              [required]="true"
            ></soft-input>
            <soft-input
              label="Mot de passe"
              type="password"
              placeholder="••••••••"
              [(ngModel)]="password"
              name="password"
              [required]="true"
            ></soft-input>

            <soft-button
              type="submit"
              [fullWidth]="true"
              [loading]="loading()"
              [disabled]="!login() || !password()"
            >
              Se connecter
            </soft-button>
          </form>
        </div>
        <p class="login__footer">© Softwell · Soft Time</p>
      </div>

      <aside class="login__brand-panel on-dark" aria-hidden="true">
        <p class="login__panel-label">Soft Time</p>
        <p class="login__panel-title">Gestion des temps &amp; heures supplémentaires</p>
        <p class="login__panel-text">Pointages, plannings et paie, au même endroit.</p>
      </aside>
    </div>
  `,
  styles: [
    `
      .login {
        min-height: 100vh;
        display: grid;
        grid-template-columns: 1fr;
        background: var(--app-surface);
      }
      .login__form-col {
        display: flex;
        flex-direction: column;
        justify-content: space-between;
        padding: 32px 24px;
        min-height: 100vh;
      }
      .login__form-wrap {
        width: 100%;
        max-width: 400px;
        margin: auto;
      }
      .login__brand {
        margin-bottom: 32px;
      }
      .login__logo {
        width: 48px;
        height: 48px;
        object-fit: contain;
      }
      .login__form-wrap h1 {
        margin: 0 0 8px;
        font-family: var(--font-heading);
        font-size: 28px;
        font-weight: 600;
        color: var(--navy-950);
        letter-spacing: -0.02em;
      }
      .login__help {
        margin: 0 0 24px;
        color: var(--text-secondary);
        font-size: 14px;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 18px;
      }
      .login__footer {
        margin: 24px 0 0;
        font-size: 12px;
        color: var(--text-secondary);
        text-align: center;
      }
      .login__brand-panel {
        display: none;
        flex-direction: column;
        justify-content: center;
        gap: 12px;
        padding: 48px;
        background: var(--sidebar-bg);
        color: var(--sidebar-brand-text);
      }
      .login__panel-label {
        margin: 0;
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 12px;
        font-weight: 600;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: var(--gold-300);
      }
      .login__panel-label::before {
        content: '';
        width: 16px;
        height: 2px;
        background: var(--gold-300);
      }
      .login__panel-title {
        margin: 0;
        font-family: var(--font-heading);
        font-size: 30px;
        font-weight: 600;
        line-height: 1.2;
        color: var(--sidebar-brand-text);
      }
      .login__panel-text {
        margin: 0;
        font-size: 14px;
        color: var(--sidebar-brand-sub);
      }
      @media (min-width: 1024px) {
        .login {
          grid-template-columns: 1fr 440px;
        }
        .login__form-col {
          padding: 48px 64px;
        }
      }
      @media (min-width: 1024px) {
        .login__brand-panel {
          display: flex;
        }
      }
    `,
  ],
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  readonly login = signal('');
  readonly password = signal('');
  readonly loading = signal(false);

  ngOnInit(): void {
    // Installation vierge : aucun compte à authentifier, on propose la création de l'administrateur.
    this.auth.isFirstRun().subscribe({
      next: (firstRun) => {
        if (firstRun) this.router.navigateByUrl('/setup', { replaceUrl: true });
      },
      error: () => undefined,
    });
  }

  submit(): void {
    if (!this.login() || !this.password() || this.loading()) return;
    this.session.clear();
    this.loading.set(true);
    this.auth.login({ login: this.login().trim(), password: this.password() }).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/dashboard']);
      },
      error: () => this.loading.set(false),
    });
  }
}
