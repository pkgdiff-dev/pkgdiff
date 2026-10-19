import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { LucideAngularModule, AlertTriangle, ExternalLink } from 'lucide-angular';

import { CompiledNotesSource } from '../../../core/models/package.models';

// Shown above notes pkgdiff compiled itself, so nobody takes them for the maintainers' own
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-compiled-notes-notice',
  standalone: true,
  imports: [LucideAngularModule],
  templateUrl: './compiled-notes-notice.html',
})
export class CompiledNotesNoticeComponent {
  readonly source = input.required<CompiledNotesSource>();

  readonly AlertTriangleIcon = AlertTriangle;
  readonly ExternalLinkIcon = ExternalLink;
}
