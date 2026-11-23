export const postCardAspectClass = 'aspect-[4/5]';

/** Brings the WoW2 glass badge tint to the console's current SDK pin. */
export const imagePillTintClass = 'bg-black/60';

/** The Posts and Processed grids share a minimum card width and gain a column when the sidebar collapses. */
export const postGalleryGridClass = [
  'grid gap-3',
  '[--post-gallery-columns:4]',
  'in-[[data-app-frame][data-rail]]:[--post-gallery-columns:5]',
  'grid-cols-[repeat(auto-fill,minmax(min(100%,max(15.5rem,calc((100%_+_0.75rem)/var(--post-gallery-columns)_-_0.75rem))),1fr))]',
].join(' ');
