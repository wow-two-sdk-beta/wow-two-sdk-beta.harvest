import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { ApiFailureFactory } from '@wow-two-beta/fe/foundation/http';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, ref, shallowRef, type ShallowRef } from 'vue';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { SourceOverviewModel, SourcePostModel, SourcePostPage } from '../src/contracts';
import SourcePostsTab from '../src/admin/harvestConsole/SourcePostsTab.vue';
import { useSourcePostPages } from '../src/admin/harvestConsole/UseSourcePostPages';
import { useSourcesView } from '../src/admin/harvestConsole/UseSourcesView';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../src/admin/harvestConsole/HarvestConsole')>()),
  useHarvestConsole: () => session,
}));
const wrappers: VueWrapper[] = [];

const source = (id = 'source-1', runId = 'run-1'): SourceOverviewModel => ({
  sourceId: id,
  name: 'Afisha',
  family: 'site',
  handle: 'afisha.uz',
  state: 'healthy',
  sections: [],
  newPostsDaily: [],
  lastHarvest: {
    runId,
    pipelineId: 'pipeline-1',
    status: 'completed',
    at: '2026-11-17T00:00:00Z',
    newPosts: 1,
    found: 1,
  },
  health: { hostKey: 'afisha.uz', minDelayMs: 5000, consecutiveFailures: 0 },
});

const post = (id: string, extra: Partial<SourcePostModel> = {}): SourcePostModel => ({
  postId: id,
  title: 'Concert ' + id,
  kind: 'event',
  state: 'live',
  revision: 1,
  firstSeenAt: '2026-11-16T00:00:00Z',
  lastChangedAt: '2026-11-16T00:00:00Z',
  language: 'ru',
  startsAt: '2026-11-18',
  url: 'https://afisha.uz/ru/' + id,
  ...extra,
});
const page = (items: SourcePostModel[], extra: Partial<SourcePostPage> = {}): SourcePostPage => ({
  items,
  total: items.length,
  searchTotal: items.length,
  nextCursor: null,
  defaultPageSize: 2,
  maxPageSize: 6,
  facets: [
    { key: 'kind', label: 'Kind', options: [{ value: 'event', label: 'Events', count: items.length }] },
    { key: 'language', label: 'Language', options: [{ value: 'ru', label: 'Russian', count: items.length }] },
    {
      key: 'eventTiming',
      label: 'Event timing',
      options: [{ value: 'upcoming', label: 'Upcoming', count: items.length }],
    },
  ],
  ...extra,
});
const ok = (value: SourcePostPage) => ResultExtensions.ok(value);
const query = () => vi.mocked(session.querySourcePosts);

beforeEach(() => {
  session = {
    client: shallowRef({}),
    containerId: ref('container-1'),
    querySourcePosts: vi.fn().mockResolvedValue(ok(page([post('one')]))),
    navigation: shallowRef({ href: () => '/post', go: vi.fn() }),
    sectionLabel: () => 'Concerts',
    sectionParts: () => ({ name: 'Concerts', locale: 'ru' }),
  } as unknown as HarvestConsole;
});
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) if (wrapper.exists()) wrapper.unmount();
  vi.useRealTimers();
});

function mountPages(current = shallowRef(source())) {
  let result!: ReturnType<typeof useSourcePostPages>;
  const component = defineComponent({
    setup() {
      const list = ref<HTMLElement | null>(null);
      result = useSourcePostPages(() => current.value, list);
      return () =>
        h('section', { style: 'overflow-y: auto; height: 200px' }, [
          h(
            'div',
            { ref: list },
            result.posts.value.map((item) => h('p', { key: item.postId }, item.title)),
          ),
        ]);
    },
  });
  const wrapper = mount(component, { attachTo: document.body });
  wrappers.push(wrapper);
  return { wrapper, result, current };
}

describe('remembered post browsing', () => {
  it('isolates source-gallery browsing when a different connection opens the same container', () => {
    const saved = useSourcesView(session);
    saved.value.search = 'private query';
    saved.value.scrollTop = 250;
    saved.value.collapsed.add('group-1');
    (session.client as ShallowRef).value = {};
    expect(saved.value.search).toBe('');
    expect(saved.value.scrollTop).toBe(0);
    expect(saved.value.collapsed.size).toBe(0);
  });

  it('restores filters, search, loaded posts and scroll after leaving the post list', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    query().mockImplementation(async (_id, selection) =>
      ok(
        page([post(selection.cursor ? 'two' : 'one')], {
          total: 2,
          searchTotal: 2,
          nextCursor: selection.cursor ? null : 'next',
        }),
      ),
    );
    const first = mountPages();
    await flushPromises();
    first.result.search.value = 'Concert';
    await vi.advanceTimersByTimeAsync(250);
    await flushPromises();
    first.result.setFilter('eventTiming', 'upcoming');
    await flushPromises();
    await first.result.loadMore(undefined, true);
    first.wrapper.element.scrollTop = 285;
    first.wrapper.unmount();
    const count = query().mock.calls.length;
    const second = mountPages();
    await flushPromises();
    expect(query()).toHaveBeenCalledTimes(count);
    expect(second.result.search.value).toBe('Concert');
    expect(second.result.filters.value).toEqual({ eventTiming: 'upcoming' });
    expect(second.result.posts.value.map((item) => item.postId)).toEqual(['one', 'two']);
    expect(second.result.browsing.value).toBe(true);
    expect(second.wrapper.element.scrollTop).toBe(285);
  });

  it('isolates remembered results by source, container and connection', async () => {
    const first = mountPages();
    await flushPromises();
    first.result.setFilter('kind', 'event');
    await flushPromises();
    first.wrapper.unmount();

    session.containerId.value = 'container-2';
    const otherContainer = mountPages();
    await flushPromises();
    expect(otherContainer.result.filters.value).toEqual({});
    otherContainer.wrapper.unmount();

    session.containerId.value = 'container-1';
    const otherSource = mountPages(shallowRef(source('source-2')));
    await flushPromises();
    expect(otherSource.result.filters.value).toEqual({});
    otherSource.wrapper.unmount();

    (session.client as ShallowRef).value = {};
    const otherConnection = mountPages();
    await flushPromises();
    expect(otherConnection.result.filters.value).toEqual({});
  });

  it('keeps the grid during search and recovers a failed update without losing results', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const { result } = mountPages();
    await flushPromises();
    query().mockResolvedValueOnce(ResultExtensions.fail(ApiFailureFactory.create('transport')));
    result.search.value = 'Missing';
    expect(result.state.value).toBe('ready');
    expect(result.updating.value).toBe(true);
    expect(result.posts.value[0]?.postId).toBe('one');
    await vi.advanceTimersByTimeAsync(250);
    await flushPromises();
    expect(result.refreshFailed.value).toBe(true);
    expect(result.state.value).toBe('ready');
    expect(result.posts.value[0]?.postId).toBe('one');
    query().mockResolvedValueOnce(ok(page([post('updated')])));
    result.retry();
    await flushPromises();
    expect(result.refreshFailed.value).toBe(false);
    expect(result.updating.value).toBe(false);
    expect(result.posts.value[0]?.postId).toBe('updated');
  });

  it('announces updates after continuation without replacing the loaded results', async () => {
    query().mockResolvedValueOnce(ok(page([post('one')], { total: 2, nextCursor: 'next' })));
    query().mockResolvedValueOnce(ok(page([post('two')], { total: 2 })));
    const { result, current } = mountPages();
    await flushPromises();
    await result.loadMore(undefined, true);
    query().mockResolvedValueOnce(ok(page([post('new'), post('one')], { total: 3 })));
    current.value = source('source-1', 'run-2');
    await flushPromises();
    expect(result.updatesAvailable.value).toBe(true);
    expect(result.posts.value.map((item) => item.postId)).toEqual(['one', 'two']);
    query().mockResolvedValueOnce(ok(page([post('new'), post('one')], { total: 3 })));
    result.retry();
    await flushPromises();
    expect(result.updatesAvailable.value).toBe(false);
    expect(result.posts.value.map((item) => item.postId)).toEqual(['new', 'one']);
  });

  it('ignores an older filter request after another selection has completed', async () => {
    const { result } = mountPages();
    await flushPromises();
    let finish!: (value: ReturnType<typeof ok>) => void;
    query().mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    result.setFilter('kind', 'event');
    const signal = query().mock.calls.at(-1)?.[2];
    query().mockResolvedValueOnce(ok(page([post('current')])));
    result.setFilter('eventTiming', 'upcoming');
    await flushPromises();
    expect(signal?.aborted).toBe(true);
    finish(ok(page([post('obsolete')])));
    await flushPromises();
    expect(result.posts.value[0]?.postId).toBe('current');
    expect(result.displayedFilters.value).toEqual({ kind: 'event', eventTiming: 'upcoming' });
  });
});

describe('post list review refinements', () => {
  it('shows removable advanced constraints, the matched count pill and the single language', async () => {
    const wrapper = mount(SourcePostsTab, {
      props: { source: { ...source(), postCount: 4 }, view: 'cards' },
      attachTo: document.body,
    });
    wrappers.push(wrapper);
    await flushPromises();
    const vm = wrapper.vm as unknown as { setFilter(key: string, value: string | null): void };
    vm.setFilter('eventTiming', 'upcoming');
    await flushPromises();
    expect(wrapper.get('[role="status"]').text()).toBe('1 of 4');
    expect(wrapper.text()).not.toContain('1 post ·');
    const remove = wrapper.get('button[aria-label="Remove Event timing: Upcoming filter"]');
    await remove.trigger('click');
    await flushPromises();
    expect(query().mock.calls.at(-1)?.[1].filters).toEqual({});
    expect(wrapper.find('ul[aria-label="Posts"] [aria-label="Russian"]').exists()).toBe(false);
    expect(wrapper.find('button[aria-label="Language"]').exists()).toBe(false);
  });

  it('shows no count pill while nothing narrows the list, and `Updating…` while a filter reloads', async () => {
    const wrapper = mount(SourcePostsTab, {
      props: { source: { ...source(), postCount: 4 }, view: 'cards' },
      attachTo: document.body,
    });
    wrappers.push(wrapper);
    await flushPromises();
    const status = () => wrapper.get('span[role="status"][aria-live="polite"]');
    expect(status().text()).toBe('');
    expect(wrapper.text()).not.toMatch(/\d+ posts?\b/);
    let finish!: (value: ReturnType<typeof ok>) => void;
    query().mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const vm = wrapper.vm as unknown as { setFilter(key: string, value: string | null): void };
    vm.setFilter('eventTiming', 'upcoming');
    await nextTick();
    expect(status().text()).toBe('Updating…');
    finish(ok(page([post('one')])));
    await flushPromises();
    expect(status().text()).toBe('1 of 4');
    vm.setFilter('eventTiming', null);
    await flushPromises();
    expect(status().text()).toBe('');
  });

  it('counts a search against the source total, and falls back to the sections when the host sends none', async () => {
    vi.useFakeTimers();
    const unsent = { ...source(), sections: [{ posts: 3 }, { posts: 2 }] } as unknown as SourceOverviewModel;
    const wrapper = mount(SourcePostsTab, { props: { source: unsent, view: 'cards' }, attachTo: document.body });
    wrappers.push(wrapper);
    await flushPromises();
    await wrapper.get('input[aria-label="Search posts"]').setValue('concert');
    await vi.advanceTimersByTimeAsync(300);
    await flushPromises();
    expect(wrapper.get('span[role="status"][aria-live="polite"]').text()).toBe('1 of 5');
  });

  it('names the language of a one-language source in a pill after the filters, and drops it once several exist', async () => {
    const wrapper = mount(SourcePostsTab, { props: { source: source(), view: 'cards' }, attachTo: document.body });
    wrappers.push(wrapper);
    await flushPromises();
    const group = wrapper.get('[role="group"][aria-label="Filter posts"]');
    const pill = group.get('[data-tone="outline"]');
    expect(pill.text()).toBe('Russian');
    expect(group.element.lastElementChild?.contains(pill.element)).toBe(true);
    expect(wrapper.find('[role="group"][aria-label="Language"]').exists()).toBe(false);

    query().mockResolvedValue(
      ok(
        page([post('one'), post('two', { language: 'uz' })], {
          facets: [
            {
              key: 'language',
              label: 'Language',
              options: [
                { value: 'ru', label: 'Russian', count: 1 },
                { value: 'uz', label: 'Uzbek', count: 1 },
              ],
            },
          ],
        }),
      ),
    );
    const several = mount(SourcePostsTab, {
      props: { source: source('source-2'), view: 'cards' },
      attachTo: document.body,
    });
    wrappers.push(several);
    await flushPromises();
    expect(several.get('[role="group"][aria-label="Filter posts"]').find('[data-tone="outline"]').exists()).toBe(false);
    expect(several.find('[role="group"][aria-label="Language"]').exists()).toBe(true);
  });

  it('shows removed state in rows and explicit event timing in both views', async () => {
    query().mockResolvedValue(ok(page([post('gone', { state: 'gone', eventTiming: 'finished' })])));
    const wrapper = mount(SourcePostsTab, { props: { source: source(), view: 'rows' }, attachTo: document.body });
    wrappers.push(wrapper);
    await flushPromises();
    expect(wrapper.get('[role="rowgroup"]').text()).toContain('removed');
    expect(wrapper.get('[role="rowgroup"]').text()).toContain('finished');
    await wrapper.setProps({ view: 'cards' });
    await nextTick();
    expect(wrapper.get('[data-event-dates]').text()).toContain('finished');
    expect(wrapper.get('[role="img"][aria-label="Removed from the source"]').attributes('tabindex')).toBe('0');
    expect(wrapper.find('[title]').exists()).toBe(false);
  });

  it('keeps the title first when a mixed-language card has a keyboard tooltip', async () => {
    const result = page([post('ru'), post('uz', { language: 'uz' })]);
    query().mockResolvedValue(
      ok({
        ...result,
        facets: result.facets.map((facet) =>
          facet.key === 'language'
            ? {
                ...facet,
                options: [
                  { value: 'ru', label: 'Russian', count: 1 },
                  { value: 'uz', label: 'Uzbek', count: 1 },
                ],
              }
            : facet,
        ),
      }),
    );
    const wrapper = mount(SourcePostsTab, { props: { source: source(), view: 'cards' }, attachTo: document.body });
    wrappers.push(wrapper);
    await flushPromises();
    const card = wrapper.get('ul[aria-label="Posts"] > li');
    const stops = card.findAll('a[href],button:not([disabled]),[tabindex="0"]');
    expect(stops[0]!.text()).toBe('Concert ru');
    const language = card.get('[tabindex="0"][aria-label="Russian"]');
    await language.trigger('focus');
    await vi.waitFor(() => expect(document.body.querySelector('[role="tooltip"]')?.textContent).toBe('Russian'));
    expect(card.find('[title]').exists()).toBe(false);
  });
});
