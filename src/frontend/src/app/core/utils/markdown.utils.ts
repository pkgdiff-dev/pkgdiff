export function stripMarkdown(text: string): string {
  return text
    .replace(/#{1,6}\s+/g, '')
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/\*(.+?)\*/g, '$1')
    .replace(/\[(.+?)\]\(.+?\)/g, '$1')
    .replace(/`{1,3}[^`]*`{1,3}/g, '')
    .replace(/^\s*[-*+]\s+/gm, '')
    .replace(/^\s*\d+\.\s+/gm, '')
    .replace(/^[-_*]{3,}$/gm, '')
    .replace(/\s+/g, ' ')
    .trim();
}

export function excerptOf(text: string, length = 160): string {
  const plain = stripMarkdown(text);
  if (plain.length <= length) return plain;

  const cut = plain.slice(0, length - 3);
  const lastSpace = cut.lastIndexOf(' ');
  return (lastSpace > length / 2 ? cut.slice(0, lastSpace) : cut) + '...';
}
