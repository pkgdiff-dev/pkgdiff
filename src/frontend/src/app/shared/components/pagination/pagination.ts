import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LucideAngularModule, ChevronLeft, ChevronRight } from 'lucide-angular';

@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './pagination.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaginationComponent {
  page = input.required<number>();
  totalPages = input.required<number>();
  total = input<number | null>(null);
  disabled = input(false);

  pageChange = output<number>();

  readonly ChevronLeftIcon = ChevronLeft;
  readonly ChevronRightIcon = ChevronRight;

  go(page: number): void {
    if (page >= 1 && page <= this.totalPages() && page !== this.page()) {
      this.pageChange.emit(page);
    }
  }
}
