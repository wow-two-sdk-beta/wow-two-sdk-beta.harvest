import type { RouteLocationRaw } from 'vue-router';
import { groupElementId, type ConsolePlace } from '@wow-two-beta/fe-harvest/admin';

/** Resolves a console place, as the shared panels name it, to this app's route. */
export function placeLocation(place: ConsolePlace): RouteLocationRaw {
  switch (place.kind) {
    case 'sources':
      return { name: 'sources', ...(place.group ? { hash: `#${groupElementId(place.group)}` } : {}) };
    case 'source':
      return {
        name: !place.tab || place.tab === 'overview' ? 'source' : `source-${place.tab}`,
        params: { id: place.id },
      };
    default:
      return { name: place.kind };
  }
}
