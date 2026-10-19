import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { LucideAngularModule, LucideIconData, Eye, EyeOff } from 'lucide-angular';

@Component({
    selector: 'app-input',
    standalone: true,
    imports: [LucideAngularModule],
    templateUrl: './input.component.html',
    styleUrls: ['./input.component.css'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    providers: [
        {
            provide: NG_VALUE_ACCESSOR,
            useExisting: forwardRef(() => InputComponent),
            multi: true
        }
    ]
})
export class InputComponent implements ControlValueAccessor {
    readonly type = input<'text' | 'password' | 'email' | 'number'>('text');
    readonly placeholder = input<string>('');
    readonly icon = input<LucideIconData | null>(null);
    readonly name = input<string>('');
    readonly autocomplete = input<string>('');
    readonly ariaLabel = input<string>('');

    readonly EyeIcon = Eye;
    readonly EyeOffIcon = EyeOff;

    // Written by the forms API from outside the template, so they are signals
    readonly value = signal('');
    readonly disabled = signal(false);
    readonly showPasswordValue = signal(false);

    readonly showPasswordToggle = computed(() => this.type() === 'password');
    readonly inputType = computed(() =>
        this.type() === 'password' && this.showPasswordValue() ? 'text' : this.type());

    private onChange: (value: string) => void = () => undefined;
    onTouch: () => void = () => undefined;

    togglePassword() {
        this.showPasswordValue.update(shown => !shown);
    }

    writeValue(value: unknown): void {
        this.value.set(value === null || value === undefined ? '' : String(value));
    }

    registerOnChange(fn: (value: string) => void): void {
        this.onChange = fn;
    }

    registerOnTouched(fn: () => void): void {
        this.onTouch = fn;
    }

    setDisabledState(isDisabled: boolean): void {
        this.disabled.set(isDisabled);
    }

    onInput(event: Event) {
        const value = (event.target as HTMLInputElement).value;
        this.value.set(value);
        this.onChange(value);
    }
}
