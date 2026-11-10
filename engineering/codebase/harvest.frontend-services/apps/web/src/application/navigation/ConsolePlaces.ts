import type { RouteLocationRaw } from 'vue-router';
import type { ConsolePlace } from '@wow-two-beta/fe-harvest/admin';

/** Resolves a console place, as the shared panels name it, to this app's route. */
export function placeLocation(place: ConsolePlace): RouteLocationRaw {
  switch (place.kind) {
    case 'pipelines':
      return { name: 'pipelines', query: place.source ? { source: place.source } : {} };
    case 'pipeline':
      return { name: 'pipeline', params: { id: place.id } };
    case 'runs':
      return { name: 'runs', query: place.pipeline ? { pipeline: place.pipeline } : {} };
    default:
      return { name: place.kind };
  }
}
