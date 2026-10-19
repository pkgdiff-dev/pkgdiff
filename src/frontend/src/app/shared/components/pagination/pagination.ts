import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LucideAngularModule, ChevronLeft, ChevronRight } from 'lucide-angular';

// Previous / next for a list that is paged on the server. Renders nothing for a single page.
//
//   <app-pagination [page]="data.page" [totalPages]="data.totalPages" [total]="data.total" (pageChange)="goToPage($event)" />
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
  // Number of items in all pages; left out of the text when it is not given
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
