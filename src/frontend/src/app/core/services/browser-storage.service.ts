import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

// localStorage that is simply empty where there is none: on the server, and in a browser that blocks it
@Injectable({ providedIn: 'root' })
export class BrowserStorage {
  private readonly storage = isPlatformBrowser(inject(PLATFORM_ID)) ? this.open() : null;

  get(key: string): string | null {
    try {
      return this.storage?.getItem(key) ?? null;
    } catch {
      return null;
    }
  }

  set(key: string, value: string): void {
    try {
      this.storage?.setItem(key, value);
    } catch {
      // A full or blocked storage loses a preference, nothing else
    }
  }

  remove(key: string): void {
    try {
      this.storage?.removeItem(key);
    } catch {
      // Nothing to remove from a storage that cannot be reached
    }
  }

  getJson<T>(key: string): T | null {
    const raw = this.get(key);
    if (raw === null) return null;
    try {
      return JSON.parse(raw) as T;
    } catch {
      return null;
    }
  }

  // Reading the property itself throws in a browser that denies storage access
  private open(): Storage | null {
    try {
      return window.localStorage;
    } catch {
      return null;
    }
  }
}
