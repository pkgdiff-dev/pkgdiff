import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-logo',
  standalone: true,
  templateUrl: './logo.html',
})
export class LogoComponent {
  size = input<'sm' | 'md' | 'lg'>('md');
}
