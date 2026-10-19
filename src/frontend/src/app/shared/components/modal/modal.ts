import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { LucideAngularModule, X } from 'lucide-angular';

export type ModalSize = 'sm' | 'md' | 'lg' | 'xl' | '2xl';

const WIDTHS: Record<ModalSize, string> = {
  sm: 'max-w-md',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
  xl: 'max-w-3xl',
  '2xl': 'max-w-6xl',
};

let nextId = 0;

// A dialog: labelled by its title, keeps the keyboard focus inside while it is open, gives it back to
// whatever had it before, and closes on Escape or a click on the backdrop. The parent decides when it
// is in the page (@if) and removes it when it emits "closed".
//
//   <app-modal title="Edit user" (closed)="close()">
//     ...body...
//     <div modalFooter>...buttons...</div>
//   </app-modal>
@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [A11yModule, LucideAngularModule],
  templateUrl: './modal.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close()' },
})
export class ModalComponent {
  title = input.required<string>();
  size = input<ModalSize>('md');
  // False while a request is under way that closing would leave without an answer
  closable = input(true);
  // On phones the dialog rises from the bottom edge, within reach of the thumb
  sheetOnMobile = input(false);

  closed = output<void>();

  readonly XIcon = X;
  readonly titleId = `app-modal-title-${nextId++}`;
  readonly width = computed(() => WIDTHS[this.size()]);
  readonly layoutClass = computed(() =>
    this.sheetOnMobile() ? 'items-end sm:items-center sm:p-4' : 'items-center p-4',
  );
  readonly panelClass = computed(() =>
    this.sheetOnMobile()
      ? `${this.width()} max-h-[90vh] sm:max-h-[85vh] rounded-t-2xl sm:rounded-xl border-x-0 border-b-0 sm:border pb-[env(safe-area-inset-bottom)] sm:pb-0`
      : `${this.width()} max-h-[85vh] rounded-xl`,
  );

  close(): void {
    if (this.closable()) {
      this.closed.emit();
    }
  }
}
