import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { LucideAngularModule, Loader } from 'lucide-angular';

@Component({
    changeDetection: ChangeDetectionStrategy.OnPush,
    selector: 'app-button',
    standalone: true,
    imports: [LucideAngularModule],
    templateUrl: './button.component.html',
    styleUrls: ['./button.component.css']
})
export class ButtonComponent {
    readonly variant = input<'primary' | 'secondary' | 'outline' | 'ghost' | 'danger'>('primary');
    readonly size = input<'sm' | 'md' | 'lg'>('md');
    readonly disabled = input<boolean>(false);
    readonly block = input<boolean>(false);
    readonly type = input<'button' | 'submit' | 'reset'>('button');
    readonly loading = input<boolean>(false);

    readonly onClick = output<MouseEvent>();

    readonly LoaderIcon = Loader;

    get classes(): string {
        const baseClasses = 'inline-flex items-center justify-center font-medium transition-colors rounded-lg focus:outline-none focus:ring-2 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed';

        // Variant styles
        const variants = {
            primary: 'bg-primary hover:bg-primary/90 text-white focus:ring-primary/40',
            secondary: 'bg-gray-100 dark:bg-gray-800 text-[#111418] dark:text-gray-200 hover:bg-gray-200 dark:hover:bg-gray-700 focus:ring-gray-200 dark:focus:ring-gray-700',
            outline: 'border border-[#dbe0e6] dark:border-gray-700 bg-transparent hover:bg-gray-50 dark:hover:bg-gray-800 text-[#111418] dark:text-gray-200 focus:ring-primary/20',
            ghost: 'bg-transparent hover:bg-gray-100 dark:hover:bg-gray-800 text-[#111418] dark:text-gray-200 focus:ring-gray-200 dark:focus:ring-gray-700',
            danger: 'bg-red-500 hover:bg-red-600 text-white focus:ring-red-500/40'
        };

        // Size styles
        const sizes = {
            sm: 'h-9 px-3 text-xs gap-1.5',
            md: 'h-11 px-4 text-sm gap-2',
            lg: 'h-14 px-6 text-base gap-3'
        };

        // Width
        const widthClass = this.block() ? 'w-full' : '';

        return `${baseClasses} ${variants[this.variant()]} ${sizes[this.size()]} ${widthClass}`;
    }

    handleClick(event: MouseEvent) {
        if (!this.disabled() && !this.loading()) {
            this.onClick.emit(event);
        }
    }
}
