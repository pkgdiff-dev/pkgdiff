// Plain text of a Markdown fragment, for excerpts and meta descriptions
export function stripMarkdown(text: string): string {
  return text
    .replace(/#{1,6}\s+/g, '')           // headings
    .replace(/\*\*(.+?)\*\*/g, '$1')     // bold
    .replace(/\*(.+?)\*/g, '$1')         // italic
    .replace(/\[(.+?)\]\(.+?\)/g, '$1')  // links
    .replace(/`{1,3}[^`]*`{1,3}/g, '')   // inline/block code
    .replace(/^\s*[-*+]\s+/gm, '')       // list items
    .replace(/^\s*\d+\.\s+/gm, '')       // ordered list
    .replace(/^[-_*]{3,}$/gm, '')        // horizontal rules
    .replace(/\s+/g, ' ')                // one space where lines and removed parts met
    .trim();
}

// At most `length` characters, cut at a word where possible
export function excerptOf(text: string, length = 160): string {
  const plain = stripMarkdown(text);
  if (plain.length <= length) return plain;

  const cut = plain.slice(0, length - 3);
  const lastSpace = cut.lastIndexOf(' ');
  return (lastSpace > length / 2 ? cut.slice(0, lastSpace) : cut) + '...';
}
