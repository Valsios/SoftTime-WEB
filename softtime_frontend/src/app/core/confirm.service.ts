import { Injectable, signal } from '@angular/core';

interface ConfirmState {
  message: string;
  title: string;
  resolve: (ok: boolean) => void;
}

@Injectable({ providedIn: 'root' })
export class ConfirmService {
  readonly state = signal<ConfirmState | null>(null);

  ask(message: string, title = 'Confirmation'): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.state.set({ message, title, resolve });
    });
  }

  answer(ok: boolean): void {
    const s = this.state();
    if (s) {
      s.resolve(ok);
      this.state.set(null);
    }
  }
}
