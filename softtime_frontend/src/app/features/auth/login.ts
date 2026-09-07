import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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
      <div class="login__panel soft-fade-in">
        <div class="login__brand">
          <img class="login__logo" src="assets/SoftTimeLogo.png" alt="Soft Time" />
          <h1>Soft Time</h1>
          <p>Gestion des temps &amp; heures supplémentaires</p>
        </div>

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

        <p class="login__hint">
          Authentification (SoftTime).
        </p>
      </div>
    </div>
  `,
  styles: [
    `
      .login {
        min-height: 100vh;
        display: grid;
        place-items: center;
        padding: 24px;
        background: radial-gradient(
            circle at 20% 20%,
            rgba(37, 99, 235, 0.12),
            transparent 45%
          ),
          radial-gradient(circle at 80% 80%, rgba(37, 99, 235, 0.08), transparent 45%),
          var(--app-bg);
      }
      .login__panel {
        width: 100%;
        max-width: 400px;
        background: var(--app-surface);
        border: 1px solid var(--app-border);
        border-radius: var(--radius-2xl);
        box-shadow: var(--shadow-lg);
        padding: 34px 30px;
      }
      .login__brand {
        text-align: center;
        margin-bottom: 26px;
      }
      .login__logo {
        display: inline-block;
        width: 72px;
        height: 72px;
        object-fit: contain;
      }
      .login__brand h1 {
        margin: 14px 0 4px;
        font-size: 24px;
      }
      .login__brand p {
        margin: 0;
        color: var(--text-secondary);
        font-size: 13px;
      }
      form {
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .login__hint {
        margin: 20px 0 0;
        text-align: center;
        font-size: 12px;
        color: var(--text-muted);
      }
    `,
  ],
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  readonly login = signal('');
  readonly password = signal('');
  readonly loading = signal(false);

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
