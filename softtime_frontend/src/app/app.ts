import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastHost } from './shared/components/toast-host';
import { ConfirmHost } from './shared/components/confirm-host';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ToastHost, ConfirmHost],
  template: `
    <router-outlet></router-outlet>
    <toast-host></toast-host>
    <confirm-host></confirm-host>
  `,
})
export class App {}
