import type { RouteLocationRaw } from 'vue-router';
import { groupElementId, type ConsolePlace } from '@wow-two-beta/fe-harvest/admin';

/** Resolves a console place, as the shared panels name it, to this app's route. */
export function placeLocation(place: ConsolePlace): RouteLocationRaw {
  switch (place.kind) {
    case 'sources':
      return { name: 'sources', ...(place.group ? { hash: `#${groupElementId(place.group)}` } : {}) };
    case 'source':
      if (place.post) return { name: 'source-post', params: { id: place.id, postId: place.post } };
      if (place.run) return { name: 'source-run', params: { id: place.id, runId: place.run } };
      return {
        name: !place.tab || place.tab === 'overview' ? 'source' : `source-${place.tab}`,
        params: { id: place.id },
      };
    case 'pipeline':
      return {
        name: `pipeline${place.stage ? '-stage' : ''}${place.run ? '-run' : place.tab && place.tab !== 'overview' ? `-${place.tab}` : ''}`,
        params: {
          id: place.id,
          ...(place.stage ? { stage: place.stage } : {}),
          ...(place.run ? { runId: place.run } : {}),
        },
      };
    case 'subscribers':
      return { name: 'subscribers', query: place.section ? { section: place.section } : {} };
    default:
      return { name: place.kind };
  }
}
