import { createLucideIcon } from 'lucide-vue-next';
import { RunwayShowIcon } from './RunwayShowIcon';

function createEventKindIcon(name: string, strokeWidth: string, nodes: Parameters<typeof createLucideIcon>[1]) {
  return createLucideIcon(
    name,
    nodes.map(([tag, attributes]) => [tag, { 'stroke-width': strokeWidth, ...attributes }]),
  );
}

/** The event kind glyphs from the approved Ocharo SVG set. */
export const eventKindIcons = {
  runway: RunwayShowIcon,
  'trade-fair': createEventKindIcon('TradeFair', '1.65', [
    ['path', { key: 'cuffs', d: 'M2.75 11.75 6.05 5.55l3 1.6-3.3 6.2Zm18.5 0-3.3-6.2-3 1.6 3.3 6.2Z' }],
    [
      'path',
      {
        key: 'right-hand',
        d:
          'M8.55 8.2h2.55m4.4-.05-2.56-.52a2.7 2.7 0 0 0-2.28.66l-2.17 2.06a1.2 1.2 0 0 0 1.58' +
          ' 1.79l1.35-1.02a.8.8 0 0 1 1 .03l4.77 3.93a1.34 1.34 0 0 1-1.69 2.07l-1.86-1.37m4.42-2.85L17' +
          ' 14.9m-1.5 2.25c.08 1.02-1.05 1.91-1.98 1.33l-.85-.6',
      },
    ],
    [
      'path',
      {
        key: 'left-hand',
        d:
          'm6.1 12.8 1.15 1.15.64-.73c.5-.57 1.3-.58 1.8-.15.5.44.56 1.05.24 1.57.52-.46 1.21-.42' +
          ' 1.65-.03.43.38.51.98.24 1.45l.94.86c.71.64.76 1.58.02 2.18-.45.36-1.02.38-1.58-.03L7.5' +
          ' 16.2c-.63-.5-.67-1.4-.25-2.25',
      },
    ],
  ]),
  market: createEventKindIcon('MarketStall', '1.75', [
    ['path', { key: 'canopy', d: 'M3 8.8 5.2 4.25h13.6L21 8.8v.65a3 3 0 0 1-6 0 3 3 0 0 1-6 0 3 3 0 0 1-6 0Z' }],
    ['path', { key: 'seams-and-posts', d: 'm9 9.45 1.15-5.2M15 9.45l-1.15-5.2M5.3 12.35v4.1m13.4-4.1v4.1' }],
    ['path', { key: 'counter', d: 'M3.8 16.45h16.4m-1.5 0v4.2H5.3v-4.2' }],
  ]),
  exhibition: createEventKindIcon('ExhibitionDressForm', '1.75', [
    [
      'path',
      {
        key: 'dress-form',
        d:
          'M10.65 2.95h2.7c0 1.6.4 2.08 1.6 2.4 1.4.38 2.48.68 2.48 1.82 0 1.9-2.03 3.27-2.03 5.15 0 1.55 1.73' +
          ' 3.12 1.73 4.62 0 .87-.48 1.31-1.48 1.31h-7.3c-1 0-1.48-.44-1.48-1.31 0-1.5 1.73-3.07 1.73-4.62' +
          ' 0-1.88-2.03-3.25-2.03-5.15 0-1.14 1.08-1.44 2.48-1.82 1.2-.32 1.6-.8 1.6-2.4Z',
      },
    ],
    ['path', { key: 'stand', d: 'M12 18.25v2.3' }],
    [
      'rect',
      {
        key: 'base',
        x: '5.3',
        y: '20',
        width: '13.4',
        height: '2.35',
        rx: '.4',
        fill: 'currentColor',
        stroke: 'none',
      },
    ],
  ]),
  competition: createEventKindIcon('CompetitionTrophy', '1.75', [
    ['path', { key: 'cup', d: 'M7 4.1h10v4.85c0 3.05-2.15 5.45-5 5.45s-5-2.4-5-5.45Z' }],
    [
      'path',
      {
        key: 'handles',
        d: 'M7 6.55H3.3v1.2c0 2.7 1.75 4.55 4.7 4.65m9-5.85h3.7v1.2c0 2.7-1.75 4.55-4.7 4.65',
      },
    ],
    ['path', { key: 'stem', d: 'M12 14.4v6m-4.35 0h8.7' }],
  ]),
  festival: createEventKindIcon('FestivalBunting', '1.7', [
    ['path', { key: 'strings', d: 'M3 4.4q9 4.75 18 0M3 13.5q8.6 4.5 18-1.1' }],
    [
      'path',
      {
        key: 'pennants',
        fill: 'currentColor',
        stroke: 'none',
        d:
          'm4.7 5.2 5.05 1.42L5.95 11.1Zm9.55 1.42 5.05-1.42-1.25 5.9ZM4.7 14.28l5.05 1.22-3.5 5Zm9.55.82' +
          ' 5-1.75-1.2 6.3Z',
      },
    ],
  ]),
  conference: createEventKindIcon('ConferenceLectern', '1.75', [
    [
      'ellipse',
      {
        key: 'microphone',
        cx: '11.75',
        cy: '4.5',
        rx: '2',
        ry: '1.27',
        transform: 'rotate(29 11.75 4.5)',
        fill: 'currentColor',
        stroke: 'none',
      },
    ],
    [
      'path',
      {
        key: 'top',
        d:
          'M13 5.3c1.6.67 2.43 1.77 2.43 3.7M7 14.25H5.4q-.65 0-.85-.66L3.4 9.85q-.23-.85.65-.85h15.9q.88 0' +
          ' .65.85l-1.15 3.74q-.2.66-.85.66h-1.6',
      },
    ],
    ['path', { key: 'lectern', d: 'm8.1 21.05-1.3-8.8h10.4l-1.3 8.8m-9.4 0h11' }],
  ]),
  other: createEventKindIcon('OtherEvent', '1.75', [
    ['rect', { key: 'top-left', x: '4.2', y: '4.2', width: '6', height: '6', rx: '1.35' }],
    ['rect', { key: 'top-right', x: '13.8', y: '4.2', width: '6', height: '6', rx: '1.35' }],
    ['rect', { key: 'bottom-left', x: '4.2', y: '13.8', width: '6', height: '6', rx: '1.35' }],
    ['rect', { key: 'bottom-right', x: '13.8', y: '13.8', width: '6', height: '6', rx: '1.35' }],
  ]),
} as const;

/** Finds the glyph for a classified event kind or extracted event format. */
export function eventKindIconOf(value?: string) {
  return value && Object.hasOwn(eventKindIcons, value)
    ? eventKindIcons[value as keyof typeof eventKindIcons]
    : undefined;
}
