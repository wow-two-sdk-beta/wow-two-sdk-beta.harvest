import { computed, nextTick, onBeforeUnmount, onMounted, onScopeDispose, ref, shallowRef, watch, type Ref } from 'vue';
import type { SourceOverviewModel, SourcePostModel, SourcePostPage, SourcePostQuery } from '../../contracts';
import { useHarvestConsole, type HarvestConsole } from './HarvestConsole';

interface BrowseSnapshot {
  search: string;
  filters: Readonly<Record<string, string>>;
  displayedFilters: Readonly<Record<string, string>>;
  posts: readonly SourcePostModel[];
  page?: SourcePostPage;
  browsing: boolean;
  scrollTop: number;
  loadedSelection: string;
  head: string;
  harvest: string;
  updatesAvailable: boolean;
}

// View data never outlives its console/client, and containers cannot share a source's remembered results.
const snapshots = new WeakMap<HarvestConsole, WeakMap<object, Map<string, BrowseSnapshot>>>();

function storeFor(session: HarvestConsole) {
  const client = session.client.value;
  if (!client) return new Map<string, BrowseSnapshot>();
  let clients = snapshots.get(session);
  if (!clients) snapshots.set(session, (clients = new WeakMap()));
  let store = clients.get(client);
  if (!store) clients.set(client, (store = new Map()));
  return store;
}

/** A revision-aware comparison ignores rotating media URLs while detecting new, removed and changed posts. */
function headOf(page: SourcePostPage) {
  return JSON.stringify([
    page.total,
    page.searchTotal,
    page.items.map((post) => [post.postId, post.revision, post.lastChangedAt, post.state, post.eventTiming]),
  ]);
}

/** Keeps results stable during updates and remembers browsing when a post or another source tab is opened. */
export function useSourcePostPages(source: () => SourceOverviewModel, list?: Ref<HTMLElement | null>) {
  const session = useHarvestConsole();
  const search = ref('');
  const filters = shallowRef<Readonly<Record<string, string>>>({});
  const posts = shallowRef<readonly SourcePostModel[]>([]);
  const page = shallowRef<SourcePostPage>();
  const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
  const browsing = ref(false);
  const updating = ref(false);
  const refreshFailed = ref(false);
  const updatesAvailable = ref(false);
  const appending = ref(false);
  const appendFailed = ref(false);
  const displayedFilters = shallowRef<Readonly<Record<string, string>>>({});
  const restoreVersion = ref(0);
  const term = computed(() => search.value.trim());
  const canAppend = computed(
    () =>
      state.value === 'ready' &&
      !!page.value?.nextCursor &&
      !appending.value &&
      !updating.value &&
      !refreshFailed.value,
  );
  let request: AbortController | undefined;
  let probe: AbortController | undefined;
  let delay: ReturnType<typeof setTimeout> | undefined;
  let mediaTimer: ReturnType<typeof setInterval> | undefined;
  let store: Map<string, BrowseSnapshot> | undefined;
  let key = '';
  let restoring = false;
  let restoreScroll = false;
  let scrollTop = 0;
  let loadedSelection = '';
  let head = '';
  let harvest = '';
  let readScroll: (() => number) | undefined;

  const harvestStamp = () => `${source().lastHarvest?.runId ?? ''}/${source().lastHarvest?.status ?? ''}`;
  function selection(): SourcePostQuery {
    return { search: term.value, filters: filters.value };
  }
  const selectionKey = () => JSON.stringify(selection());

  function cancel() {
    clearTimeout(delay);
    request?.abort();
    probe?.abort();
    probe = undefined;
  }

  function remember() {
    if (!store || !key) return;
    store.set(key, {
      search: search.value,
      filters: filters.value,
      displayedFilters: displayedFilters.value,
      posts: posts.value,
      page: page.value,
      browsing: browsing.value,
      scrollTop: readScroll?.() ?? scrollTop,
      loadedSelection,
      head,
      harvest,
      updatesAvailable: updatesAvailable.value,
    });
  }

  async function firstPage(quiet = false) {
    const controller = new AbortController();
    request = controller;
    const query = selection();
    const queryKey = selectionKey();
    const stamp = harvestStamp();
    const result = await session.querySourcePosts(source().sourceId, query, controller.signal);
    if (controller.signal.aborted) return;
    updating.value = false;
    if (result.ok) {
      page.value = result.value;
      posts.value = result.value.items;
      displayedFilters.value = query.filters ?? {};
      loadedSelection = queryKey;
      head = headOf(result.value);
      harvest = stamp;
      state.value = 'ready';
      browsing.value = false;
      refreshFailed.value = false;
      updatesAvailable.value = false;
      appendFailed.value = false;
    } else if (page.value) {
      refreshFailed.value = true;
    } else if (!quiet) {
      state.value = 'unavailable';
    }
  }

  async function checkForUpdates() {
    if (appending.value || updating.value || probe) return;
    const controller = new AbortController();
    probe = controller;
    const result = await session.querySourcePosts(source().sourceId, selection(), controller.signal);
    if (controller.signal.aborted) return;
    probe = undefined;
    if (result.ok) {
      updatesAvailable.value = headOf(result.value) !== head;
      refreshFailed.value = false;
    } else {
      refreshFailed.value = true;
    }
  }

  function refresh() {
    if (state.value !== 'ready' || updating.value || appending.value) return;
    if (browsing.value) void checkForUpdates();
    else {
      cancel();
      void firstPage(true);
    }
  }

  function retry() {
    cancel();
    refreshFailed.value = false;
    updating.value = !!page.value;
    if (!page.value) state.value = 'loading';
    void firstPage();
  }

  watch(
    [() => source().sourceId, session.client, session.containerId],
    () => {
      remember();
      cancel();
      store = storeFor(session);
      key = `${session.containerId.value}/${source().sourceId}`;
      const saved = store.get(key);
      restoring = true;
      search.value = saved?.search ?? '';
      filters.value = saved?.filters ?? {};
      posts.value = saved?.posts ?? [];
      page.value = saved?.page;
      browsing.value = saved?.browsing ?? false;
      scrollTop = saved?.scrollTop ?? 0;
      loadedSelection = saved?.loadedSelection ?? '';
      displayedFilters.value = saved?.displayedFilters ?? {};
      head = saved?.head ?? '';
      harvest = saved?.harvest ?? '';
      updatesAvailable.value = saved?.updatesAvailable ?? false;
      appending.value = false;
      appendFailed.value = false;
      refreshFailed.value = false;
      updating.value = false;
      state.value = page.value ? 'ready' : 'loading';
      restoreScroll = !!saved;
      restoreVersion.value++;
      restoring = false;
      if (!saved?.page || loadedSelection !== selectionKey()) retry();
      else if (harvest !== harvestStamp()) updatesAvailable.value = true;
    },
    { immediate: true, flush: 'sync' },
  );
  watch(
    [term, () => filters.value],
    ([text], previous) => {
      if (restoring) return;
      cancel();
      updating.value = !!page.value;
      refreshFailed.value = false;
      appending.value = false;
      appendFailed.value = false;
      updatesAvailable.value = false;
      if (!page.value) state.value = 'loading';
      if (previous && previous[0] !== text) delay = setTimeout(() => void firstPage(), 250);
      else void firstPage();
    },
    { flush: 'sync' },
  );
  watch([() => source().lastHarvest?.runId, () => source().lastHarvest?.status], refresh);

  if (list) {
    watch(
      [list, restoreVersion],
      async ([element], _previous, onCleanup) => {
        if (!element) return;
        const win = element.ownerDocument.defaultView;
        if (!win) return;
        let parent = element.parentElement;
        while (parent && !/^(auto|scroll|overlay)$/.test(win.getComputedStyle(parent).overflowY))
          parent = parent.parentElement;
        const scroller = parent ?? element.ownerDocument.scrollingElement;
        if (!scroller) return;
        let attached = true;
        readScroll = () => scroller.scrollTop;
        onCleanup(() => {
          attached = false;
          readScroll = undefined;
        });
        if (restoreScroll) {
          await nextTick();
          if (!attached) return;
          scroller.scrollTop = scrollTop;
          restoreScroll = false;
        }
      },
      { flush: 'post' },
    );
  }
  onMounted(() => {
    mediaTimer = setInterval(refresh, 60_000);
  });
  onBeforeUnmount(remember);
  onScopeDispose(() => {
    cancel();
    clearInterval(mediaTimer);
  });

  function setFilter(key: string, value: string | null) {
    if ((filters.value[key] ?? null) === value) return;
    const next = { ...filters.value };
    if (value) next[key] = value;
    else delete next[key];
    filters.value = next;
  }

  function clearFilters() {
    if (Object.keys(filters.value).length) filters.value = {};
  }

  async function loadMore(limit?: number, manually = false): Promise<boolean> {
    const cursor = page.value?.nextCursor;
    if (!cursor || !canAppend.value || (appendFailed.value && !manually)) return false;
    cancel();
    const controller = new AbortController();
    request = controller;
    browsing.value = true;
    appending.value = true;
    appendFailed.value = false;
    const result = await session.querySourcePosts(
      source().sourceId,
      { ...selection(), cursor, ...(limit ? { limit } : {}) },
      controller.signal,
    );
    if (controller.signal.aborted) return false;
    appending.value = false;
    if (!result.ok) {
      appendFailed.value = true;
      return false;
    }
    const known = new Set(posts.value.map((post) => post.postId));
    posts.value = [...posts.value, ...result.value.items.filter((post) => !known.has(post.postId))];
    page.value = result.value;
    return true;
  }

  return {
    search,
    filters,
    displayedFilters,
    posts,
    page,
    state,
    browsing,
    updating,
    refreshFailed,
    updatesAvailable,
    appending,
    appendFailed,
    canAppend,
    setFilter,
    clearFilters,
    loadMore,
    retry,
  };
}
