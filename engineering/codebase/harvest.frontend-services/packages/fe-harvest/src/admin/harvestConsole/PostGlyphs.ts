import type { ReadMethod } from './SourceWords';

/** The line glyphs of the post pills, drawn on a 24-unit grid with a 2-unit stroke. */
export const Glyphs = {
  event: 'M4 6h16v14H4z M4 10h16 M8 3v4 M16 3v4',
  dates: 'M4 6h16v14H4z M4 10h16 M8 3v4 M16 3v4 M8 15h8',
  article: 'M5 4h11v16H7a2 2 0 0 1-2-2z M16 8h3v10a2 2 0 0 1-2 2 M8 8h5 M8 12h5 M8 16h3',
  other: 'M6 3h9l4 4v14H6z M14 3v5h5 M9 13h7 M9 17h5',
  unlink: 'M7 17 17 7M8 7h9v9M4 4l16 16',
  offer: 'M3 12 12 3h8v8l-9 9z M16 8h.01',
  exhibition: 'M4 5h16v14H4z M9 10a1.5 1.5 0 1 0 0 .01 M20 16l-5-5-8 8',
  fashion: 'M12 8V6.5a2 2 0 1 0-2-2 M12 8 3.5 14.5A1.5 1.5 0 0 0 4.5 17h15a1.5 1.5 0 0 0 1-2.5z',
  theatre: 'M4 4h10v8a5 5 0 0 1-10 0z M7.5 8.5h.01 M10.5 8.5h.01 M7 12.5c1 1 2.5 1 3.5 0 M16 8h4v6a4 4 0 0 1-6 3.5',
  concert: 'M9 18V6l10-2v12 M9 18a3 3 0 1 1-3-3 M19 16a3 3 0 1 1-3-3',
  section: 'M4 7h6l2 2h8v10H4z',
  language: 'M4 5h9 M8.5 3v2 M6 5c.5 3 2.5 6 6 7.5 M12 5c-.5 3-2.5 6-6 7.5 M13 21l4-9 4 9 M14.5 18h5',
  history: 'M3 12a9 9 0 1 0 3-6.7M3 4v5h5M12 7.5V12l3 2',
  site: 'M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 3a4 9 0 1 0 0 18a4 9 0 1 0 0-18 M3 12h18',
  telegram: 'M21 4 3 11l6 2 2 6 3-4 5 4z m-12 9 8-6',
  instagram:
    'M9 4h6a5 5 0 0 1 5 5v6a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5V9a5 5 0 0 1 5-5z M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M17 7h.01',
  social: 'M4 5h16v11H9l-5 4z',
  branch: 'M4 10 12 4l8 6v10H4z M10 20v-6h4v6',
} as const satisfies Record<string, string>;

export type GlyphName = keyof typeof Glyphs;

/** The glyph of a post's kind: `event`, `article`, an `offer` on a marketplace, else the catch-all dots. */
export function kindGlyphOf(kind: string): GlyphName {
  if (kind === 'event') return 'event';
  if (kind === 'article' || kind === 'news' || kind === 'post') return 'article';
  if (kind === 'offer' || kind === 'listing' || kind === 'ad') return 'offer';
  return 'other';
}

/**
 * The glyph of a section, from the topic its name carries: exhibitions, fashion, theatre, concerts, events; any other
 * name gets the folder. The adapter's section list carries a name only, so the topic is read from it.
 */
export function sectionGlyphOf(name: string): GlyphName {
  const topic = name.toLowerCase();
  if (/exhibit|gallery|museum|art\b/.test(topic)) return 'exhibition';
  if (/fashion|style|wear|clothes/.test(topic)) return 'fashion';
  if (/theat|drama|opera|ballet/.test(topic)) return 'theatre';
  if (/concert|music|festival|live/.test(topic)) return 'concert';
  if (/event|show|afisha|what.?s on/.test(topic)) return 'event';
  return 'section';
}

/** The glyph of how a source is read, for a source pill. */
export function readMethodGlyphOf(method: ReadMethod): GlyphName {
  return method;
}
