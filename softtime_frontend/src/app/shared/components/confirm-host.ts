import { ChangeDetectionStrategy, Component, HostListener, inject } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { SoftButton } from './soft-button';

@Component({
  selector: 'confirm-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftButton],
  template: `
    @if (confirm.state(); as s) {
      <div class="soft-modal__overlay" (click)="onOverlay($event)">
        <div class="soft-modal__panel soft-modal__panel--sm">
          <div class="soft-modal__header">
            <h3>{{ s.title }}</h3>
            <button class="soft-modal__close" type="button" (click)="confirm.answer(false)">
              &times;
            </button>
          </div>
          <div class="soft-modal__body">
            <p>{{ s.message }}</p>
          </div>
          <div class="soft-modal__footer">
            <soft-button variant="ghost" (click)="confirm.answer(false)">Annuler</soft-button>
            <soft-button variant="danger" (click)="confirm.answer(true)">{{ s.okText }}</soft-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmHost {
  readonly confirm = inject(ConfirmService);

  onOverlay(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('soft-modal__overlay')) {
      this.confirm.answer(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.confirm.state()) {
      this.confirm.answer(false);
    }
  }
}
