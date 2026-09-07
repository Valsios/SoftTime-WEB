import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { Droit, Role } from '../../shared/models';
import { PrivilegesService, RolesService } from '../../shared/services/users.service';
import { PageHeader, SoftButton, SoftCard, SoftSelect } from '../../shared/components';

@Component({
  selector: 'app-privileges',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftSelect],
  template: `
    <page-header title="Privilèges" subtitle="Droits par rôle">
      <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      <soft-select
        label="Rôle"
        [(ngModel)]="selectedRoleId"
        name="roleId"
        [options]="roleOptions()"
        (ngModelChange)="onRoleChange($event)"
      ></soft-select>

      <div class="droit-list">
        @for (d of droits(); track d.id) {
          <label class="droit-item">
            <input type="checkbox" [checked]="selected().has(d.id)" (change)="toggle(d.id, $event)" />
            <span>{{ d.nom }} <small class="muted">({{ d.id }})</small></span>
          </label>
        }
      </div>
    </soft-card>
  `,
  styles: [
    `
      .droit-list { margin-top: 20px; display: grid; gap: 10px; }
      .droit-item { display: flex; align-items: center; gap: 10px; font-weight: 500; cursor: pointer; }
    `,
  ],
})
export class PrivilegesPage implements OnInit {
  private readonly rolesSvc = inject(RolesService);
  private readonly privSvc = inject(PrivilegesService);
  private readonly toast = inject(ToastService);

  readonly droits = signal<Droit[]>([]);
  readonly roleOptions = signal<{ value: number; label: string }[]>([]);
  readonly selected = signal<Set<number>>(new Set());
  readonly saving = signal(false);
  selectedRoleId: number | null = null;

  ngOnInit(): void {
    this.privSvc.droits().subscribe((d) => this.droits.set(d));
    this.rolesSvc.list().subscribe((roles) => {
      this.roleOptions.set(roles.map((r) => ({ value: r.id, label: r.nom })));
      if (roles.length) {
        this.selectedRoleId = roles[0].id;
        this.onRoleChange(roles[0].id);
      }
    });
  }

  onRoleChange(roleId: unknown): void {
    const id = Number(roleId);
    if (!id) return;
    this.privSvc.byRole(id).subscribe((privs) => {
      this.selected.set(new Set(privs.map((p) => p.droitId)));
    });
  }

  toggle(droitId: number, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selected.update((set) => {
      const next = new Set(set);
      if (checked) next.add(droitId);
      else next.delete(droitId);
      return next;
    });
  }

  save(): void {
    if (!this.selectedRoleId) return;
    this.saving.set(true);
    this.privSvc.replace(this.selectedRoleId, [...this.selected()]).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Privilèges enregistrés.'); },
      error: () => this.saving.set(false),
    });
  }
}
