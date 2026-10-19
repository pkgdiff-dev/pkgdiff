import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-grid-pattern',
  standalone: true,
  templateUrl: './grid-pattern.html',
})
export class GridPatternComponent {}
