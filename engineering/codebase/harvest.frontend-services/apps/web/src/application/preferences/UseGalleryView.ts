import type { Ref } from 'vue';
import { createPersistentRef } from '@wow-two-beta/fe/foundation/storage';
import type { GalleryView } from '@wow-two-beta/fe-harvest/admin';

/** The `localStorage` key the gallery view lives under, beside `scfd.theme` and `scfd.sidebar-collapsed`. */
export const GalleryViewKey = 'scfd.gallery-view';

const galleryView = createPersistentRef<GalleryView>(GalleryViewKey, 'cards', {
  decode: (stored) => (stored === 'cards' || stored === 'rows' ? stored : undefined),
});

/** Cards or Rows: one remembered choice for the Sources page and a source's Posts tab. */
export function useGalleryView(): Ref<GalleryView> {
  return galleryView;
}
