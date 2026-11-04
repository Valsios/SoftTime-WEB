import { Injectable, effect, inject } from '@angular/core';
import { SessionStore } from '../../core/session.store';

export type PrivilegesDraftMode = 'user' | 'role';

export interface PrivilegesDraftState {
  mode: PrivilegesDraftMode;
  targetId: number;
  baseline: Set<string>;
  working: Set<string>;
  selectedRoleId: number | null;
  expandedGroups: string[];
  search: string;
}

@Injectable({ providedIn: 'root' })
export class PrivilegesDraftService {
  private readonly session = inject(SessionStore);
  private readonly drafts = new Map<string, PrivilegesDraftState>();
  private lastUserId: number | null;
  private activeTarget: { mode: PrivilegesDraftMode; targetId: number } | null = null;

  constructor() {
    this.lastUserId = this.session.session()?.userId ?? null;
    effect(() => {
      const userId = this.session.session()?.userId ?? null;
      if (this.lastUserId !== userId) {
        this.clearAll();
      }
      this.lastUserId = userId;
    });
  }

  loadBaseline(
    mode: PrivilegesDraftMode,
    targetId: number,
    permissions: Iterable<string>,
    selectedRoleId: number | null = null,
  ): PrivilegesDraftState {
    const baseline = new Set(permissions);
    const state: PrivilegesDraftState = {
      mode,
      targetId,
      baseline,
      working: new Set(baseline),
      selectedRoleId,
      expandedGroups: [],
      search: '',
    };
    this.drafts.set(this.key(mode, targetId), state);
    return state;
  }

  refreshBaseline(
    mode: PrivilegesDraftMode,
    targetId: number,
    serverPermissions: Iterable<string>,
  ): { baseline: Set<string>; working: Set<string>; dirty: boolean; serverChanged: boolean } {
    const newBaseline = new Set(serverPermissions);
    const existing = this.get(mode, targetId);
    if (!existing) {
      const state = this.loadBaseline(mode, targetId, newBaseline);
      return {
        baseline: new Set(state.baseline),
        working: new Set(state.working),
        dirty: false,
        serverChanged: false,
      };
    }

    const isModified = !this.samePermissions(existing.baseline, existing.working);
    if (!isModified) {
      existing.baseline = newBaseline;
      existing.working = new Set(newBaseline);
      return {
        baseline: new Set(existing.baseline),
        working: new Set(existing.working),
        dirty: false,
        serverChanged: false,
      };
    }

    const serverChanged = !this.samePermissions(existing.baseline, newBaseline);
    existing.baseline = newBaseline;
    return {
      baseline: new Set(existing.baseline),
      working: new Set(existing.working),
      dirty: true,
      serverChanged,
    };
  }

  get(mode: PrivilegesDraftMode, targetId: number): PrivilegesDraftState | undefined {
    return this.drafts.get(this.key(mode, targetId));
  }

  setActive(mode: PrivilegesDraftMode, targetId: number): void {
    this.activeTarget = { mode, targetId };
  }

  getActive(): { mode: PrivilegesDraftMode; targetId: number } | null {
    return this.activeTarget ? { ...this.activeTarget } : null;
  }

  dirtyTargets(): { mode: PrivilegesDraftMode; targetId: number }[] {
    const result: { mode: PrivilegesDraftMode; targetId: number }[] = [];
    for (const draft of this.drafts.values()) {
      if (!this.samePermissions(draft.baseline, draft.working)) {
        result.push({ mode: draft.mode, targetId: draft.targetId });
      }
    }
    return result;
  }

  update(mode: PrivilegesDraftMode, targetId: number, permissions: Iterable<string>): void {
    const draft = this.get(mode, targetId);
    if (!draft) return;
    draft.working = new Set(permissions);
  }

  discard(mode: PrivilegesDraftMode, targetId: number): void {
    const draft = this.getRequired(mode, targetId);
    draft.working = new Set(draft.baseline);
  }

  markSaved(mode: PrivilegesDraftMode, targetId: number, savedPermissions?: Iterable<string>): void {
    const draft = this.get(mode, targetId);
    if (!draft) return;
    if (savedPermissions !== undefined) {
      draft.baseline = new Set(savedPermissions);
    } else {
      draft.baseline = new Set(draft.working);
    }
  }

  clearTarget(mode: PrivilegesDraftMode, targetId: number): void {
    this.drafts.delete(this.key(mode, targetId));
  }

  clearAll(): void {
    this.drafts.clear();
    this.activeTarget = null;
  }

  /**
   * Sans argument : vrai si au moins un brouillon diffère de sa base de référence.
   * Avec (mode, targetId) : vrai uniquement pour ce brouillon.
   */
  hasDirty(mode?: PrivilegesDraftMode, targetId?: number): boolean {
    if (mode !== undefined && targetId !== undefined) {
      const draft = this.get(mode, targetId);
      return !!draft && !this.samePermissions(draft.baseline, draft.working);
    }
    return [...this.drafts.values()].some((draft) => !this.samePermissions(draft.baseline, draft.working));
  }

  /** Nombre de droits ajoutés et retirés par rapport à la base de référence. */
  changesCount(mode: PrivilegesDraftMode, targetId: number): number {
    const draft = this.get(mode, targetId);
    if (!draft) return 0;
    let count = 0;
    draft.working.forEach((permission) => {
      if (!draft.baseline.has(permission)) count++;
    });
    draft.baseline.forEach((permission) => {
      if (!draft.working.has(permission)) count++;
    });
    return count;
  }

  private getRequired(mode: PrivilegesDraftMode, targetId: number): PrivilegesDraftState {
    const draft = this.get(mode, targetId);
    if (!draft) {
      throw new Error('Brouillon de privilèges introuvable.');
    }
    return draft;
  }

  private key(mode: PrivilegesDraftMode, targetId: number): string {
    return `${mode}:${targetId}`;
  }

  private samePermissions(left: Set<string>, right: Set<string>): boolean {
    return left.size === right.size && [...left].every((permission) => right.has(permission));
  }
}
