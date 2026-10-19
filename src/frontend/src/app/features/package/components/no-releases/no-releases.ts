import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LucideAngularModule, Info, ExternalLink } from 'lucide-angular';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-no-releases',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './no-releases.html',
})
export class NoReleasesComponent {
  readonly InfoIcon = Info;
  readonly ExternalLinkIcon = ExternalLink;
}
