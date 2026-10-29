import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorMessage, apiFieldErrors } from '../../core/api-error';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { PasswordPolicy, Role, User, PageChangeEvent } from '../../shared/models';
import { CardPaieService } from '../../shared/services/catalog.service';
import { PasswordPolicyService } from '../../shared/services/password-policy.service';
import { RolesService, UsersService } from '../../shared/services/users.service';
import {
  Column,
  DataTable,
  PageHeader,
  PasswordPolicyHint,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftSelect,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';
import {
  FALLBACK_PASSWORD_POLICY,
  evaluatePasswordPolicy,
  isPasswordCompliant,
} from '../../shared/utils/password-policy';

type FieldKey = 'nom' | 'login' | 'password' | 'roleId';
type FieldErrors = Record<FieldKey, string>;

const EMPTY_FIELD_ERRORS: FieldErrors = { nom: '', login: '', password: '', roleId: '' };
const API_FIELD_KEYS: Record<string, FieldKey> = {
  nom: 'nom',
  login: 'login',
  password: 'password',
  roleid: 'roleId',
};
const MESSAGES_REQUIS: FieldErrors = {
  nom: 'Le nom est obligatoire.',
  login: 'Le login est obligatoire.',
  password: 'Le mot de passe saisi est invalide.',
  roleId: 'Le rôle est obligatoire.',
};

@Component({
  selector: 'app-users',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect, PasswordPolicyHint],
  template: `
    <page-header title="Utilisateurs" subtitle="Gestion RESPONSABLE">
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"
        [serverSide]="true" [total]="total()" [serverPage]="page()" [serverPageSize]="pageSize"
        [(searchTerm)]="tableSearch" (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)">
        <ng-template #actions let-row>
          <div class="soft-actions" style="justify-content: end">
            <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal #userModal [open]="modal()" [title]="editId() ? 'Modifier utilisateur' : 'Nouvel utilisateur'" (closed)="close()">
      @if (modal()) {
        @if (formError()) {
          <div class="form-error" role="alert">{{ formError() }}</div>
        }
        <div class="form-grid">
          <soft-input
            label="Nom"
            [required]="true"
            [(ngModel)]="form.nom"
            (ngModelChange)="onFieldChange()"
            name="nom"
            [error]="errors().nom"
          ></soft-input>
          <soft-input
            label="Login"
            [required]="true"
            [(ngModel)]="form.login"
            (ngModelChange)="onFieldChange()"
            name="login"
            [error]="errors().login"
          ></soft-input>
          <soft-input label="E-mail" type="email" [(ngModel)]="form.email" name="email" placeholder="Envoi des identifiants"></soft-input>
          <soft-select label="Matricule" [(ngModel)]="form.matricule" name="matricule" placeholder="Aucun" [options]="matriculeOptions()"></soft-select>
          <div class="form-grid__password">
            <soft-input
              label="Mot de passe"
              type="password"
              [(ngModel)]="form.password"
              (ngModelChange)="onFieldChange()"
              name="password"
              [required]="!editId()"
              [error]="errors().password"
              [placeholder]="editId() ? 'Laisser vide pour conserver' : '••••••••'"
            ></soft-input>
            @if (!errors().password) {
              <password-policy-hint
                [password]="form.password ?? ''"
                [policy]="policy()"
              ></password-policy-hint>
            }
          </div>
          <soft-select
            label="Rôle"
            [required]="true"
            [(ngModel)]="form.roleId"
            (ngModelChange)="onFieldChange()"
            name="roleId"
            placeholder="Sélectionner un rôle"
            [options]="roleOptions()"
            [hint]="roleOptions().length ? '' : 'Aucun rôle disponible : créez un rôle avant de créer un utilisateur.'"
            [error]="errors().roleId"
          ></soft-select>
        </div>
      }
      @if (modal()) {
        <div footer>
          <soft-button variant="ghost" [disabled]="saving()" (click)="close()">Annuler</soft-button>
          <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
        </div>
      }
    </soft-modal>
  `,
  styles: [
    `
      .form-grid { display: grid; gap: 14px; }
      .form-grid__password { display: grid; gap: 8px; }
      .form-error {
        margin-bottom: 14px;
        padding: 10px 12px;
        border: 1px solid var(--danger-border);
        border-radius: var(--radius-md);
        background: var(--danger-bg);
        color: var(--danger);
        font-size: 13px;
        font-weight: 500;
        line-height: 1.4;
      }
    `,
  ],
})
export class UsersPage implements OnInit {
  private readonly svc = inject(UsersService);
  private readonly rolesSvc = inject(RolesService);
  private readonly cards = inject(CardPaieService);
  private readonly passwordPolicies = inject(PasswordPolicyService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  private readonly modalHost = viewChild('userModal', { read: ElementRef });

  readonly canCreate = computed(() => this.session.hasPermission('PARAMS:USERS:CREATE', Droit.Parameters));
  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:USERS:UPDATE', Droit.Parameters));
  readonly canDelete = computed(() => this.session.hasPermission('PARAMS:USERS:DELETE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  private requestSeq = 0;
  readonly roleOptions = signal<{ value: number; label: string }[]>([]);
  readonly matriculeOptions = signal<{ value: string; label: string }[]>([]);
  readonly policy = signal<PasswordPolicy | null>(null);
  readonly tableSearch = signal('');
  form: Partial<User> = {};

  readonly submitted = signal(false);
  readonly serverErrors = signal<Partial<FieldErrors>>({});
  readonly formError = signal('');
  private readonly formVersion = signal(0);

  /** Erreurs de validation locales, recalculées à chaque saisie et au chargement de la politique. */
  private readonly localErrors = computed<FieldErrors>(() => {
    this.formVersion();
    const nom = (this.form.nom ?? '').trim();
    const login = (this.form.login ?? '').trim();
    return {
      nom: nom ? '' : 'Le nom est obligatoire.',
      login: login ? '' : 'Le login est obligatoire.',
      password: this.passwordError(),
      roleId: this.roleIdValid() ? '' : 'Le rôle est obligatoire.',
    };
  });

  /** Erreurs affichées sous les champs : locales, complétées par les erreurs renvoyées par l'API. */
  readonly errors = computed<FieldErrors>(() => {
    if (!this.submitted()) return EMPTY_FIELD_ERRORS;
    const local = this.localErrors();
    const server = this.serverErrors();
    return {
      nom: local.nom || server.nom || '',
      login: local.login || server.login || '',
      password: local.password || server.password || '',
      roleId: local.roleId || server.roleId || '',
    };
  });

  readonly isValid = computed(() => {
    const errors = this.localErrors();
    return !errors.nom && !errors.login && !errors.password && !errors.roleId;
  });

  readonly columns: Column[] = [
    { key: 'nom', label: 'Nom' },
    { key: 'login', label: 'Login' },
    { key: 'email', label: 'E-mail' },
    { key: 'matricule', label: 'Matricule' },
    { key: 'roleName', label: 'Rôle' },
  ];

  ngOnInit(): void {
    this.passwordPolicies.get().subscribe((policy) => this.policy.set(policy));
    this.rolesSvc.list().subscribe({
      next: (roles) => {
        this.roleOptions.set(roles.map((r) => ({ value: r.id, label: r.nom })));
        this.loadPage();
      },
      error: () => this.loadPage(),
    });
    this.cards.list().subscribe({
      next: (list) =>
        this.matriculeOptions.set(
          [...new Set(list.map((c) => c.sageMatricule).filter((m): m is string => !!m))]
            .sort((a, b) => a.localeCompare(b, 'fr', { numeric: true }))
            .map((m) => ({ value: m, label: m })),
        ),
      error: () => undefined,
    });
  }

  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize, this.tableSearch()).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        const roles = this.roleOptions();
        this.rows.set(
          r.items.map((u) =>
            asRow({
              ...u,
              roleName: roles.find((role) => Number(role.value) === Number(u.roleId))?.label ?? String(u.roleId ?? ''),
            }),
          ),
        );
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: () => { if (seq === this.requestSeq) this.loading.set(false); },
    });
  }

  onPageChange(event: PageChangeEvent): void {
    this.page.set(event.page);
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  onTableSearch(term: string): void {
    this.tableSearch.set(term);
    this.page.set(1);
    this.loadPage();
  }

  openCreate(): void {
    this.editId.set(null);
    this.form = {};
    this.resetValidation();
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = { ...(row as unknown as User), password: null };
    this.resetValidation();
    this.modal.set(true);
  }

  close(): void {
    this.modal.set(false);
    this.form = {};
    this.saving.set(false);
    this.resetValidation();
  }

  onFieldChange(): void {
    this.formVersion.update((version) => version + 1);
    this.serverErrors.set({});
    this.formError.set('');
  }

  save(): void {
    if (this.saving()) return;
    this.submitted.set(true);
    this.serverErrors.set({});
    this.formError.set('');
    if (!this.isValid()) {
      this.focusFirstInvalid();
      return;
    }

    const id = this.editId();
    const dto: User = {
      ...(this.form as User),
      nom: (this.form.nom ?? '').trim(),
      login: (this.form.login ?? '').trim(),
      roleId: Number(this.form.roleId),
      password: this.form.password ?? null,
    };
    this.saving.set(true);
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.close();
        this.toast.success('Enregistré.');
        this.loadPage();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.applyServerError(err);
        this.focusFirstInvalid();
      },
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cet utilisateur ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.loadPage(); } });
  }

  /** Un mot de passe saisi doit respecter la politique ; en modification, un champ vide conserve l'ancien. */
  private passwordError(): string {
    const password = this.form.password ?? '';
    if (!password.trim()) {
      return this.editId() ? '' : 'Le mot de passe est obligatoire.';
    }
    const checks = evaluatePasswordPolicy(password, this.policy() ?? FALLBACK_PASSWORD_POLICY);
    if (isPasswordCompliant(checks)) return '';
    const manquantes = checks.filter((check) => !check.ok).map((check) => lowerFirst(check.label));
    return `Mot de passe non conforme : ${joinFr(manquantes)}.`;
  }

  private roleIdValid(): boolean {
    const roleId = Number(this.form.roleId);
    return Number.isFinite(roleId) && roleId > 0;
  }

  private resetValidation(): void {
    this.submitted.set(false);
    this.serverErrors.set({});
    this.formError.set('');
    this.formVersion.update((version) => version + 1);
  }

  /** Affiche les erreurs renvoyées par l'API sous le champ concerné, sinon dans le bandeau du popup. */
  private applyServerError(err: HttpErrorResponse): void {
    const { fields, trusted } = apiFieldErrors(err);
    const mapped: Partial<FieldErrors> = {};
    const inconnues: string[] = [];

    for (const [key, message] of Object.entries(fields)) {
      const field = API_FIELD_KEYS[key];
      if (!field) {
        inconnues.push(message);
        continue;
      }
      mapped[field] = trusted[key] ? message : MESSAGES_REQUIS[field];
    }

    this.serverErrors.set(mapped);
    const aDesChamps = Object.keys(mapped).length > 0;
    this.formError.set(
      inconnues.length
        ? inconnues.join(' ')
        : aDesChamps
          ? ''
          : apiErrorMessage(err) || "L'enregistrement a échoué. Veuillez réessayer.",
    );
  }

  private focusFirstInvalid(): void {
    setTimeout(() => {
      const panel = this.modalHost()?.nativeElement;
      const invalid = panel?.querySelector(
        '.soft-field--error .soft-input, .soft-field--error .soft-select select',
      );
      invalid?.focus();
    });
  }
}

function lowerFirst(value: string): string {
  return value.length ? value.charAt(0).toLowerCase() + value.slice(1) : value;
}

function joinFr(parts: readonly string[]): string {
  if (parts.length <= 1) return parts[0] ?? '';
  return `${parts.slice(0, -1).join(', ')} et ${parts[parts.length - 1]}`;
}
