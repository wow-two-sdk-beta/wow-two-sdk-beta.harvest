import { computed, reactive } from 'vue';
import type { HarvestConsole } from './HarvestConsole';

interface SourcesView {
  search: string;
  filter: string;
  activeOnly: boolean;
  collapsed: Set<string>;
  scrollTop: number;
}

const views = new WeakMap<HarvestConsole, WeakMap<object, Map<string, SourcesView>>>();

/** Remembers one Sources view per connected session and container while browsing its detail pages. */
export function useSourcesView(session: HarvestConsole) {
  const state = computed(() => {
    const client = session.client.value;
    let clients = views.get(session);
    if (!clients) views.set(session, (clients = new WeakMap()));
    let containers = client ? clients.get(client) : undefined;
    if (!containers) {
      containers = new Map();
      if (client) clients.set(client, containers);
    }
    const key = session.containerId.value;
    let saved = containers.get(key);
    if (!saved) {
      saved = reactive({ search: '', filter: 'all', activeOnly: true, collapsed: new Set<string>(), scrollTop: 0 });
      containers.set(key, saved);
    }
    return saved;
  });
  return state;
}
