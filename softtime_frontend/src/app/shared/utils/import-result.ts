import { ToastService } from '../../core/toast.service';
import { ImportResult } from '../models';

/**
 * Affiche le bilan d'un import : le message du backend contient déjà les compteurs
 * (importés / ignorés). On passe en avertissement, plus long, dès qu'une ligne a été ignorée.
 */
export function toastImportResult(toast: ToastService, r: ImportResult, fallback: string): void {
  const message = r.message?.trim() || fallback;
  if (r.skipped > 0) {
    toast.show(message, 'warning', 8000);
  } else if (r.imported > 0) {
    toast.success(message);
  } else {
    toast.info(message);
  }
}
