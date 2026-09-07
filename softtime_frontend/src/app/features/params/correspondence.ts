import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { CorrespondenceModeService } from '../../shared/services/catalog.service';
import { PageHeader, SoftButton, SoftCard, SoftToggle } from '../../shared/components';

@Component({
  selector: 'app-correspondence',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftToggle],
  template: `
    <page-header title="Mode correspondance" subtitle="Correspondance manuelle SAGE ↔ badge">
      <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      @if (loading()) {
        <div class="soft-spinner-lg"></div>
      } @else {
        <label class="toggle-row">
          <div>
            <strong>Correspondance manuelle active</strong>
            <p class="muted">Désactive l'auto-mapping matricule / SSN</p>
          </div>
          <soft-toggle [(ngModel)]="active" name="active"></soft-toggle>
        </label>
      }
    </soft-card>
  `,
  styles: [`.toggle-row { display:flex; align-items:center; justify-content:space-between; gap:16px; } .toggle-row p { margin:4px 0 0; font-size:13px; }`],
})
export class CorrespondencePage implements OnInit {
  private readonly svc = inject(CorrespondenceModeService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  active = false;

  ngOnInit(): void {
    this.svc.get().subscribe({
      next: (m) => { this.active = m.active; this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    this.saving.set(true);
    this.svc.update({ active: this.active }).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Mode enregistré.'); },
      error: () => this.saving.set(false),
    });
  }
}
