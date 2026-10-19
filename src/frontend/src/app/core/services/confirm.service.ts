import { Injectable, signal } from '@angular/core';

export interface ConfirmConfig {
    title: string;
    message: string;
    confirmText?: string;
    cancelText?: string;
    type?: 'danger' | 'warning' | 'info';
}

@Injectable({
    providedIn: 'root'
})
export class ConfirmService {
    readonly config = signal<ConfirmConfig | null>(null);

    private resolveRef: ((value: boolean) => void) | null = null;

    confirm(message: string, title = 'Are you sure?', type: 'danger' | 'warning' | 'info' = 'danger'): Promise<boolean> {
        this.config.set({
            title,
            message,
            confirmText: 'Confirm',
            cancelText: 'Cancel',
            type
        });

        return new Promise((resolve) => {
            this.resolveRef = resolve;
        });
    }

    resolve(result: boolean) {
        this.config.set(null);
        if (this.resolveRef) {
            this.resolveRef(result);
            this.resolveRef = null;
        }
    }
}
