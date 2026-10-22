import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { ClockParamsService } from '../../shared/services/catalog.service';
import { PageHeader, SoftButton, SoftCard, SoftInput, SoftToggle } from '../../shared/components';

@Component({
  selector: 'app-clock-params',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftInput, SoftToggle],
  template: `
    <page-header title="Paramètres pointeuse" subtitle="Appariement des pointages et fiabilité du traitement">
      <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      @if (loading()) {
        <div class="soft-spinner-lg"></div>
      } @else {
        <h3>Mode d'appariement</h3>
        <label class="toggle-row">
          <div>
            <strong>Multi-pointeuse</strong>
            <p class="muted">
              Appariement par paires multiples (seuil de 15 minutes) au lieu du premier → dernier
              pointage. Le mode simple s'applique quand cette option est désactivée.
            </p>
          </div>
          <soft-toggle [(ngModel)]="multiPoint" name="multiPoint"></soft-toggle>
        </label>

        <h3>Fiabilité du mode simple</h3>
        <label class="toggle-row">
          <div>
            <strong>Mode strict</strong>
            <p class="muted">
              Dédoublonne les badges, n'accepte que 2 ou 4 pointages cohérents, signale le reste en
              anomalie motivée et ne compte plus un jour en attente d'arbitrage comme une absence.
              Désactivé = comportement historique.
            </p>
          </div>
          <soft-toggle [(ngModel)]="simpleStrict" name="simpleStrict"></soft-toggle>
        </label>
        <label class="toggle-row">
          <div>
            <strong>Déduire la pause pointée</strong>
            <p class="muted">
              Utilise l'écart pause → reprise d'une journée à 4 pointages au lieu de la pause de la
              catégorie de personnel. Modifie les heures calculées : à activer en connaissance de cause.
            </p>
          </div>
          <soft-toggle [(ngModel)]="simpleHpSpe" name="simpleHpSpe"></soft-toggle>
        </label>
        <div class="numbers">
          <soft-input label="Durée minimale d'une journée (minutes)" type="number" [(ngModel)]="dureeMinJour" name="dureeMinJour"></soft-input>
          <soft-input label="Durée maximale d'une journée (minutes)" type="number" [(ngModel)]="dureeMaxJour" name="dureeMaxJour"></soft-input>
        </div>
        <p class="muted">0 ou vide = pas de contrôle. En mode strict, une journée hors bornes devient une anomalie.</p>

        <h3>Périmètre</h3>
        <label class="toggle-row">
          <div>
            <strong>Filtrer sur la base pointeuse active</strong>
            <p class="muted">
              Ignore les pointages importés d'une autre base. Les lignes historiques sans base
              renseignée restent prises en compte.
            </p>
          </div>
          <soft-toggle [(ngModel)]="filtrePointeuse" name="filtrePointeuse"></soft-toggle>
        </label>
        <label class="toggle-row">
          <div>
            <strong>Signaler les pointages hors planning</strong>
            <p class="muted">
              Un pointage un week-end ou un jour non planifié produit une anomalie au lieu d'être ignoré
              (uniquement pour les salariés ayant un planning sur la période).
            </p>
          </div>
          <soft-toggle [(ngModel)]="anomalieHorsPlanning" name="anomalieHorsPlanning"></soft-toggle>
        </label>
        <label class="toggle-row">
          <div>
            <strong>Postes de nuit</strong>
            <p class="muted">
              Regroupe les pointages par vacation (sortie le lendemain) au lieu de la date calendaire.
            </p>
          </div>
          <soft-toggle [(ngModel)]="simpleNuit" name="simpleNuit"></soft-toggle>
        </label>
      }
    </soft-card>
  `,
  styles: [
    `
      .toggle-row { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
      .toggle-row p { margin: 4px 0 0; font-size: 13px; }
      h3 { margin: 18px 0 10px; font-size: 15px; }
      h3:first-of-type { margin-top: 0; }
      .numbers { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; margin-top: 14px; }
      .muted { color: var(--text-muted); font-size: 13px; }
    `,
  ],
})
export class ClockParamsPage implements OnInit {
  private readonly svc = inject(ClockParamsService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  multiPoint = false;
  simpleStrict = false;
  simpleHpSpe = false;
  filtrePointeuse = false;
  anomalieHorsPlanning = false;
  simpleNuit = false;
  dureeMinJour: number | null = null;
  dureeMaxJour: number | null = null;
  private clockId = 0;

  ngOnInit(): void {
    this.svc.get().subscribe({
      next: (c) => {
        this.clockId = c.id;
        this.multiPoint = !!c.multiPoint;
        this.simpleStrict = !!c.simpleStrict;
        this.simpleHpSpe = !!c.simpleHpSpe;
        this.filtrePointeuse = !!c.filtrePointeuse;
        this.anomalieHorsPlanning = !!c.anomalieHorsPlanning;
        this.simpleNuit = !!c.simpleNuit;
        this.dureeMinJour = c.dureeMinJour && c.dureeMinJour > 0 ? c.dureeMinJour : null;
        this.dureeMaxJour = c.dureeMaxJour && c.dureeMaxJour > 0 ? c.dureeMaxJour : null;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    this.saving.set(true);
    this.svc.update({
      id: this.clockId,
      multiPoint: this.multiPoint,
      simpleStrict: this.simpleStrict,
      simpleHpSpe: this.simpleHpSpe,
      filtrePointeuse: this.filtrePointeuse,
      anomalieHorsPlanning: this.anomalieHorsPlanning,
      simpleNuit: this.simpleNuit,
      // 0 désactive le contrôle, comme une valeur vide.
      dureeMinJour: this.dureeMinJour ?? 0,
      dureeMaxJour: this.dureeMaxJour ?? 0,
    }).subscribe({
      next: () => { this.saving.set(false); this.toast.success('Paramètres enregistrés.'); },
      error: () => this.saving.set(false),
    });
  }
}
