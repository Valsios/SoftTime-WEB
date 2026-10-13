import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '../../core/toast.service';
import { AdminSetupRequest } from '../../shared/models';
import { AuthService } from '../../shared/services/auth.service';
import { SoftButton } from '../../shared/components/soft-button';
import { SoftInput } from '../../shared/components/soft-input';

/**
 * Écran de démarrage initial : crée le premier compte administrateur d'une installation vierge.
 * Accessible sans authentification ; si un compte existe déjà, l'écran renvoie vers la connexion
 * (le serveur refuse de toute façon une seconde création).
 */
@Component({
  selector: 'app-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftButton, SoftInput],
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
            <soft-input label="Mot de passe" type="password" placeholder="••••••••" [(ngModel)]="form.password" name="password" [required]="true"></soft-input>
            <soft-input label="E-mail (facultatif)" type="email" placeholder="admin@exemple.com" [(ngModel)]="form.email" name="email"></soft-input>

            <soft-button
              type="submit"
              [fullWidth]="true"
              [loading]="creating()"
              [disabled]="!form.nom || !form.login || !form.password"
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
        background: radial-gradient(circle at 20% 20%, rgba(37, 99, 235, 0.12), transparent 45%),
          radial-gradient(circle at 80% 80%, rgba(37, 99, 235, 0.08), transparent 45%),
          var(--app-bg);
      }
      .setup__panel {
        width: 100%;
        max-width: 420px;
        background: var(--app-surface);
        border: 1px solid var(--app-border);
        border-radius: var(--radius-2xl);
        box-shadow: var(--shadow-lg);
        padding: 34px 30px;
      }
      .setup__brand {
        text-align: center;
        margin-bottom: 22px;
      }
      .setup__logo {
        width: 84px;
        height: 84px;
        object-fit: contain;
      }
      .setup__brand h1 {
        margin: 8px 0 4px;
        font-size: 24px;
      }
      .setup__brand p {
        margin: 0;
        color: var(--app-text-muted);
        font-size: 14px;
      }
      .setup__hint {
        margin: 14px 0 0;
        color: var(--app-text-muted);
        font-size: 13px;
        text-align: center;
      }
    `,
  ],
})
export class SetupPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly creating = signal(false);
  form: AdminSetupRequest = { nom: '', login: '', password: '', email: '' };

  ngOnInit(): void {
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

  submit(): void {
    if (this.creating()) return;
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
