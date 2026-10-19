import { ChangeDetectionStrategy, Component, input, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LucideAngularModule, AlertCircle, ArrowRight, ArrowLeft } from 'lucide-angular';
import { PackageService } from '../../../../core/services/package.service';
import { PackageSuggestion } from '../../../../core/models/package.models';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-package-not-found',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './package-not-found.html',
})
export class PackageNotFoundComponent implements OnInit {
  packageName = input.required<string>();
  registry = input<string>('nuget');

  readonly AlertIcon = AlertCircle;
  readonly ArrowRightIcon = ArrowRight;
  readonly ArrowLeftIcon = ArrowLeft;

  private router = inject(Router);
  private packageService = inject(PackageService);

  suggestions = signal<PackageSuggestion[]>([]);
  loadingSuggestions = signal(false);

  ngOnInit(): void {
    this.loadSuggestions();
  }

  private loadSuggestions(): void {
    const query = this.packageName();
    if (!query || query.length < 2) return;

    this.loadingSuggestions.set(true);
    this.packageService.getSuggestions(this.registry(), query).subscribe({
      next: (results) => {
        this.suggestions.set(results);
        this.loadingSuggestions.set(false);
      },
      error: () => {
        this.loadingSuggestions.set(false);
      },
    });
  }

  goToPackage(packageId: string): void {
    this.router.navigate(['/package', this.registry(), packageId]);
  }

  goHome(): void {
    this.router.navigate(['/']);
  }
}
