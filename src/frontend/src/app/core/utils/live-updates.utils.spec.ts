import { LiveUpdate } from '../models/package.models';
import { formatAge, mergeLiveUpdate } from './live-updates.utils';

const entry = (packageId: string, updatedAt: string, overrides: Partial<LiveUpdate> = {}): LiveUpdate => ({
  packageId,
  registry: 'npm',
  version: '1.0.0',
  previousVersion: null,
  updatedAt,
  ...overrides,
});

describe('mergeLiveUpdate', () => {
  const items = [entry('b', '2026-10-02T10:00:00Z'), entry('a', '2026-10-01T10:00:00Z')];

  it('puts a new package first when it is the most recent', () => {
    const merged = mergeLiveUpdate(items, entry('c', '2026-10-03T10:00:00Z'), 20);

    expect(merged.map((item) => item.packageId)).toEqual(['c', 'b', 'a']);
  });

  it('replaces the entry of a package that is already listed', () => {
    const update = entry('a', '2026-10-03T10:00:00Z', { version: '1.1.0', previousVersion: '1.0.0' });

    const merged = mergeLiveUpdate(items, update, 20);

    expect(merged.map((item) => item.packageId)).toEqual(['a', 'b']);
    expect(merged[0].version).toBe('1.1.0');
  });

  it('keeps the same package name of another registry as its own entry', () => {
    const merged = mergeLiveUpdate(items, entry('a', '2026-10-03T10:00:00Z', { registry: 'pypi' }), 20);

    expect(merged.map((item) => `${item.registry}/${item.packageId}`)).toEqual(['pypi/a', 'npm/b', 'npm/a']);
  });

  it('orders by date, so an update with an older date is not shown as the newest', () => {
    const merged = mergeLiveUpdate(items, entry('c', '2026-09-01T10:00:00Z'), 20);

    expect(merged.map((item) => item.packageId)).toEqual(['b', 'a', 'c']);
  });

  it('never grows beyond the limit and does not change the list it was given', () => {
    const merged = mergeLiveUpdate(items, entry('c', '2026-10-03T10:00:00Z'), 2);

    expect(merged.map((item) => item.packageId)).toEqual(['c', 'b']);
    expect(items.map((item) => item.packageId)).toEqual(['b', 'a']);
  });
});

describe('formatAge', () => {
  const now = new Date('2026-10-04T12:00:00Z');

  it.each([
    ['2026-10-04T11:59:40Z', 'now'],
    ['2026-10-04T11:15:00Z', '45m'],
    ['2026-10-04T05:00:00Z', '7h'],
    ['2026-10-01T12:00:00Z', '3d'],
    ['2026-07-04T12:00:00Z', '3mo'],
    ['2024-10-04T12:00:00Z', '2y'],
    ['2026-10-04T12:05:00Z', 'now'],
    ['not a date', ''],
  ])('formats %s as "%s"', (updatedAt, expected) => {
    expect(formatAge(updatedAt, now)).toBe(expected);
  });
});
