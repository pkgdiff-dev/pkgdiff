import { LiveUpdate } from '../models/package.models';

const sameEntry = (a: LiveUpdate, b: LiveUpdate) =>
  a.packageId === b.packageId && a.registry === b.registry;

// A package appears once: a pushed update replaces its entry and the list stays newest first
export function mergeLiveUpdate(items: LiveUpdate[], update: LiveUpdate, limit: number): LiveUpdate[] {
  return [update, ...items.filter((item) => !sameEntry(item, update))]
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
    .slice(0, limit);
}

export function formatAge(updatedAt: string, now: Date = new Date()): string {
  const minutes = Math.floor((now.getTime() - Date.parse(updatedAt)) / 60000);
  if (Number.isNaN(minutes)) {
    return '';
  }
  if (minutes < 1) {
    return 'now';
  }
  if (minutes < 60) {
    return `${minutes}m`;
  }
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours}h`;
  }
  const days = Math.floor(hours / 24);
  if (days < 30) {
    return `${days}d`;
  }
  if (days < 365) {
    return `${Math.floor(days / 30)}mo`;
  }
  return `${Math.floor(days / 365)}y`;
}
