import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { ClockParamsService } from '../../shared/services/catalog.service';
import { PageHeader, SoftButton, SoftCard, SoftToggle } from '../../shared/components';

@Component({
  selector: 'app-clock-params',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftToggle],
  template: `
    <page-header title="Paramètres pointeuse" subtitle="Mode multi-pointeuse">
      <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      @if (loading()) {
        <div class="soft-spinner-lg"></div>
      } @else {
        <label class="toggle-row">
          <div>
            <strong>Multi-pointeuse</strong>
            <p class="muted">Autoriser plusieurs bases pointeuse actives</p>
          </div>
          <soft-toggle [(ngModel)]="multiPoint" name="multiPoint"></soft-toggle>
        </label>
      }
    </soft-card>
  `,
  styles: [
    `
      .toggle-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
      .toggle-row p { margin: 4px 0 0; font-size: 13px; }
    `,
  ],
})
export class ClockParamsPage implements OnInit {
  private readonly svc = inject(ClockParamsService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  multiPoint = false;
  private clockId = 0;

  ngOnInit(): void {
    this.svc.get().subscribe({
      next: (c) => {
        this.clockId = c.id;
        this.multiPoint = !!c.multiPoint;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    this.saving.set(true);
    this.svc.update({ id: this.clockId, multiPoint: this.multiPoint }).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Paramètres enregistrés.'); },
      error: () => this.saving.set(false),
    });
  }
}
