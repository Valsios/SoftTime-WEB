import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import type { PasswordPolicy } from '../models';
import {
  FALLBACK_PASSWORD_POLICY,
  evaluatePasswordPolicy,
  isPasswordCompliant,
} from '../utils/password-policy';

/**
 * Affiche en direct les règles de mot de passe respectées / non respectées.
 * Les libellés proviennent de la politique du serveur, pour ne jamais diverger.
 */
@Component({
  selector: 'password-policy-hint',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (password()) {
      <ul class="pwd-policy" role="list">
        @for (rule of checks(); track rule.code) {
          <li [class.is-ok]="rule.ok" [class.is-ko]="!rule.ok">
            <span class="pwd-policy__icon" aria-hidden="true">{{ rule.ok ? '✓' : '✗' }}</span>
            <span>{{ rule.label }}</span>
            <span class="pwd-policy__sr">{{ rule.ok ? ': règle respectée' : ': règle non respectée' }}</span>
          </li>
        }
      </ul>
      @if (valid()) {
        <p class="pwd-policy__status" role="status">Mot de passe conforme aux règles de sécurité.</p>
      }
    } @else {
      <p class="pwd-policy__hint">{{ summary() }}</p>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .pwd-policy {
        display: grid;
        gap: 4px;
        margin: 2px 0 0;
        padding: 0;
        list-style: none;
        font-size: 12px;
        line-height: 1.35;
      }
      .pwd-policy li {
        position: relative;
        display: flex;
        align-items: flex-start;
        gap: 7px;
        color: var(--text-muted);
      }
      .pwd-policy li.is-ok {
        color: var(--success);
      }
      .pwd-policy li.is-ko {
        color: var(--danger);
      }
      .pwd-policy__icon {
        flex: 0 0 12px;
        font-weight: 700;
        text-align: center;
      }
      .pwd-policy__sr {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
      .pwd-policy__status {
        margin: 7px 0 0;
        color: var(--success);
        font-size: 12px;
        font-weight: 600;
      }
      .pwd-policy__hint {
        margin: 2px 0 0;
        color: var(--text-muted);
        font-size: 12px;
        line-height: 1.4;
      }
    `,
  ],
})
export class PasswordPolicyHint {
  readonly password = input('');
  readonly policy = input<PasswordPolicy | null>(null);

  private readonly effectivePolicy = computed(() => this.policy() ?? FALLBACK_PASSWORD_POLICY);

  readonly checks = computed(() => evaluatePasswordPolicy(this.password(), this.effectivePolicy()));

  readonly valid = computed(() => isPasswordCompliant(this.checks()));

  readonly summary = computed(
    () =>
      `Au moins ${this.effectivePolicy().minLength} caractères, avec majuscule, minuscule, ` +
      'chiffre et caractère spécial.',
  );
}
