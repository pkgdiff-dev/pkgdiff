export type PageLink = { kind: 'page'; page: number } | { kind: 'gap'; key: string };

const SIBLINGS = 1;

export function pageWindow(current: number, total: number): PageLink[] {
  let start = Math.max(2, current - SIBLINGS);
  let end = Math.min(total - 1, current + SIBLINGS);
  if (start === 3) start = 2;
  if (end === total - 2) end = total - 1;

  const links: PageLink[] = [{ kind: 'page', page: 1 }];
  if (start > 2) links.push({ kind: 'gap', key: 'gap-start' });
  for (let page = start; page <= end; page++) links.push({ kind: 'page', page });
  if (end < total - 1) links.push({ kind: 'gap', key: 'gap-end' });
  if (total > 1) links.push({ kind: 'page', page: total });
  return links;
}
