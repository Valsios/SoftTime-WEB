import { Injectable, signal } from '@angular/core';
import { shouldShowToast } from './toast.logic';

export type ToastKind = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private seq = 0;
  private readonly lastShown = new Map<string, number>();

  show(message: string, kind: ToastKind = 'info', durationMs = 4000): void {
    // Un même message d'erreur déjà affiché dans les 3 dernières secondes n'est pas répété.
    if (!shouldShowToast(this.lastShown, kind, message, Date.now())) return;
    const id = ++this.seq;
    this.toasts.update((list) => [...list, { id, kind, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }

  success(message: string): void {
    this.show(message, 'success');
  }
  error(message: string): void {
    this.show(message, 'error', 6000);
  }
  warning(message: string): void {
    this.show(message, 'warning');
  }
  info(message: string): void {
    this.show(message, 'info');
  }

  dismiss(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
