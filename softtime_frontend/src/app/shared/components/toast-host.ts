import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '../../core/toast.service';

@Component({
  selector: 'toast-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="soft-toasts">
      @for (toast of toasts.toasts(); track toast.id) {
        <div class="soft-toast" [class]="'soft-toast--' + toast.kind" (click)="toasts.dismiss(toast.id)">
          {{ toast.message }}
        </div>
      }
    </div>
  `,
})
export class ToastHost {
  readonly toasts = inject(ToastService);
}
