import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';
import { LucideAngularModule, AlertTriangle, AlertCircle, Info } from 'lucide-angular';
import { ConfirmService } from '../../../core/services/confirm.service';

// The one confirmation dialog of the application, driven by ConfirmService. Escape and a click on
// the backdrop answer "no".
@Component({
    selector: 'app-confirm-modal',
    standalone: true,
    imports: [A11yModule, LucideAngularModule],
    templateUrl: './confirm-modal.component.html',
    styleUrl: './confirm-modal.component.css',
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { '(document:keydown.escape)': 'cancel()' },
})
export class ConfirmModalComponent {
    service = inject(ConfirmService);

    readonly AlertIcon = AlertTriangle;
    readonly AlertCircleIcon = AlertCircle;
    readonly InfoIcon = Info;

    confirm() {
        this.service.resolve(true);
    }

    cancel() {
        if (this.service.config()) {
            this.service.resolve(false);
        }
    }
}
