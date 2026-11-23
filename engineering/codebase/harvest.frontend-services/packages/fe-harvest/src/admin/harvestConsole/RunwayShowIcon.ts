import { createLucideIcon } from 'lucide-vue-next';

/** The approved Runway show glyph, sized by the icon's caller. */
export const RunwayShowIcon = createLucideIcon('RunwayShow', [
  ['circle', { key: 'head', cx: '12', cy: '4.2', r: '2.05', fill: 'currentColor', stroke: 'none' }],
  [
    'path',
    {
      key: 'arms',
      'stroke-width': '1.65',
      d:
        'M10 8.15c-1.63 1.1-2.36 3.08-2.22 4.72.1 1.1.72 1.66 1.86 2m4.36-6.72c1.63 1.1 2.36 3.08 2.22' +
        ' 4.72-.1 1.1-.72 1.66-1.86 2',
    },
  ],
  [
    'path',
    {
      key: 'model',
      fill: 'currentColor',
      stroke: 'none',
      d:
        'M9.85 7.6h4.3c.7 0 1.1.58.9 1.25l-1.1 3.65 3.2 4.8c-3.1 1.7-7.2 1.7-10.3' +
        ' 0l3.2-4.8-1.1-3.65c-.2-.67.2-1.25.9-1.25Z',
    },
  ],
  ['path', { key: 'runway', d: 'M5.55 15.35 3 21h18l-2.55-5.65' }],
]);
