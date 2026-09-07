import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  input,
  output,
} from '@angular/core';

@Component({
  selector: 'soft-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div class="soft-modal__overlay" (click)="onOverlay($event)">
        <div class="soft-modal__panel" [class]="'soft-modal__panel--' + size()">
          <div class="soft-modal__header">
            <h3>{{ title() }}</h3>
            <button class="soft-modal__close" type="button" (click)="closed.emit()">
              &times;
            </button>
          </div>
          <div class="soft-modal__body">
            <ng-content></ng-content>
          </div>
          <div class="soft-modal__footer">
            <ng-content select="[footer]"></ng-content>
          </div>
        </div>
      </div>
    }
  `,
})
export class SoftModal {
  readonly open = input(false);
  readonly title = input('');
  readonly size = input<'sm' | 'md' | 'lg' | 'xl'>('md');
  readonly closed = output<void>();

  onOverlay(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('soft-modal__overlay')) {
      this.closed.emit();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) this.closed.emit();
  }
}
