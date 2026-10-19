import { ChangeDetectionStrategy, Component, output, inject } from '@angular/core';
import { Router } from '@angular/router';
import { LucideAngularModule, AlertCircle, RefreshCw, ArrowLeft } from 'lucide-angular';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-service-unavailable',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './service-unavailable.html',
})
export class ServiceUnavailableComponent {
  private router = inject(Router);

  readonly AlertIcon = AlertCircle;
  readonly RefreshIcon = RefreshCw;
  readonly ArrowLeftIcon = ArrowLeft;

  onRetry = output<void>();

  retry(): void {
    this.onRetry.emit();
  }

  goHome(): void {
    this.router.navigate(['/']);
  }
}
