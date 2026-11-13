import { flushPromises, mount } from '@vue/test-utils';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, type VNodeChild } from 'vue';
import HarvestConnectPanel from '../src/admin/harvestConsole/HarvestConnectPanel.vue';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import HomePanel from '../src/admin/harvestConsole/HomePanel.vue';
import PostPanel from '../src/admin/harvestConsole/PostPanel.vue';
import SourcePanel from '../src/admin/harvestConsole/SourcePanel.vue';
import SourcesGallery from '../src/admin/harvestConsole/SourcesGallery.vue';
import AccessPanel from '../src/admin/harvestConsole/AccessPanel.vue';
import type { HarvestConsoleOptions } from '../src/admin';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
/** The fixture's harvests are dated 2026-11-10, so the clock is pinned there: "today" labels stay the same any day. */
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-11-10T12:00:00Z'));
});
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  window.sessionStorage.clear();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

/** The adapter's sections of the fixture source: Fashion is harvested, Theatre and Concerts are not. */
const sections = [
  { label: 'Fashion', url: 'https://www.afisha.uz/ru/fashion' },
  { label: 'Theatre', url: 'https://www.afisha.uz/ru/theatre' },
  { label: 'Concerts', url: 'https://www.afisha.uz/ru/concerts' },
];

/** Afisha.uz, harvested: its last harvest found 4 new of 23 and stopped at its page limit. */
const harvested = {
  sourceId: 'source-1',
  name: 'Afisha.uz',
  family: 'site',
  handle: 'www.afisha.uz',
  groupId: 'group-1',
  state: 'healthy',
  lastHarvest: {
    runId: 'run-1',
    pipelineId: 'pipeline-1',
    at: '2026-11-10T08:12:00Z',
    status: 'partial',
    reason: 'page_limit',
    newPosts: 4,
    found: 23,
  },
  nextHarvestAt: '2026-11-10T14:12:00Z',
  newPostsDaily: [0, 1, 0, 2, 0, 0, 3, 1, 0, 0, 1, 0, 2, 4],
  sections: [
    {
      pipelineId: 'pipeline-1',
      name: 'Afisha · Ocharo',
      url: 'https://www.afisha.uz/ru/fashion',
      harvesting: true,
      paused: false,
      captureEvery: 'PT6H',
      checkEvery: 'P1D',
      maxPages: 5,
      lastRun: {
        runId: 'run-1',
        pipelineId: 'pipeline-1',
        kind: 'capture',
        at: '2026-11-10T08:12:00Z',
        status: 'partial',
        reason: 'page_limit',
        newPosts: 4,
        found: 23,
      },
      nextDueAt: '2026-11-10T14:12:00Z',
      posts: 4,
      subscribers: 1,
    },
  ],
  health: { hostKey: 'www.afisha.uz', minDelayMs: 5000, consecutiveFailures: 0 },
};

/** Afisha Telegram, never harvested. */
const idle = {
  sourceId: 'source-2',
  name: 'Afisha Telegram',
  family: 'telegram',
  handle: 'afisha_uz',
  groupId: 'group-1',
  state: 'idle',
  newPostsDaily: Array.from({ length: 14 }, () => 0),
  sections: [],
  health: { hostKey: 't.me', minDelayMs: 5000, consecutiveFailures: 0 },
};

/** Replies as a small complete service, with no real network; `calls` records every write. */
/** `groups: 2` files the idle Telegram source under a second group, so the page shows group tiles. */
function fixtureFetch({ groups = 1 }: { groups?: 1 | 2 } = {}) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
    const method = input instanceof Request ? input.method : (init?.method ?? 'GET');
    const path = url.pathname;
    if (path.endsWith('/capabilities'))
      return Response.json({
        data: {
          sourceId: 'source-1',
          adapterKey: 'afisha-uz',
          supportsPathScope: true,
          workKinds: ['capture', 'check'],
          configuration: { displayName: 'Afisha', readiness: 'ready', scopeSchema: { sections } },
        },
      });
    if (path === '/api/pipelines' && method === 'POST')
      return Response.json({
        data: {
          id: 'pipeline-2',
          name: 'Afisha.uz · Theatre',
          state: 'active',
          sourceId: 'source-1',
          scope: {},
          schedules: [],
        },
      });
    if (path === '/api/runs' && method === 'POST') return Response.json({ data: { id: 'run-9', status: 'queued' } });
    const body =
      path === '/api/containers'
        ? [{ id: 'container-1', kind: 'pool' }]
        : path === '/api/sources'
          ? [{ id: 'source-1', handle: 'www.afisha.uz', family: 'site', state: 'active', coverage: 'filtered' }]
          : path === '/api/sources/overview'
            ? [harvested, groups === 2 ? { ...idle, groupId: 'group-2' } : idle]
            : path === '/api/sources/groups'
              ? [
                  { id: 'group-1', containerId: 'container-1', name: 'Afisha', description: 'Event listings.' },
                  ...(groups === 2 ? [{ id: 'group-2', containerId: 'container-1', name: 'Telegram channels' }] : []),
                ]
              : path === '/api/pipelines'
                ? [
                    {
                      id: 'pipeline-1',
                      name: 'Afisha · Ocharo',
                      state: 'active',
                      sourceId: 'source-1',
                      scope: { url: 'https://www.afisha.uz/ru/fashion', restrictToPath: true, parameters: {} },
                      limits: { maxPages: 5 },
                      steps: {},
                      schedules: [
                        {
                          id: 's-1',
                          kind: 'capture',
                          mode: 'interval',
                          every: 'PT6H',
                          enabled: true,
                          timeZone: 'Asia/Tashkent',
                        },
                      ],
                    },
                  ]
                : path === '/api/runs'
                  ? [
                      {
                        id: 'run-1',
                        pipelineId: 'pipeline-1',
                        kind: 'capture',
                        status: 'failed',
                        captured: 0,
                        unchanged: 0,
                        discovered: 0,
                        failed: 1,
                        fetchOutcome: 'timeout',
                        message: 'the host did not answer within 30 seconds',
                        queuedAtUtc: '2026-11-10T08:00:00Z',
                      },
                    ]
                  : path === '/api/sources/source-1/posts'
                    ? [
                        {
                          postId: 'post-1',
                          title: 'Fashion Show Line 2026',
                          kind: 'event',
                          state: 'live',
                          revision: 2,
                          firstSeenAt: '2026-11-10T08:11:00Z',
                          lastChangedAt: '2026-11-10T08:11:00Z',
                          pipelineId: 'pipeline-1',
                          startsAt: '2099-11-21T12:00:00Z',
                          url: 'https://www.afisha.uz/ru/fashion/show',
                          imageUrl: 'https://www.afisha.uz/media/show-1.jpg',
                          imageUrls: [
                            'https://www.afisha.uz/media/show-1.jpg',
                            'https://www.afisha.uz/media/show-2.jpg',
                          ],
                        },
                        {
                          postId: 'post-2',
                          title: 'A guide to the season’s fabrics',
                          kind: 'article',
                          state: 'live',
                          revision: 1,
                          firstSeenAt: '2026-11-10T08:12:00Z',
                          lastChangedAt: '2026-11-10T08:12:00Z',
                          pipelineId: 'pipeline-1',
                        },
                      ]
                    : path === '/api/posts/post-1'
                      ? {
                          id: 'post-1',
                          containerId: 'container-1',
                          state: 'live',
                          revision: 2,
                          base: {
                            schema: 'wow2.post.v1',
                            kind: 'event',
                            title: 'Fashion Show Line 2026',
                            media: [
                              { kind: 'image', url: 'https://www.afisha.uz/media/show-1.jpg' },
                              { kind: 'video', url: 'https://www.afisha.uz/media/teaser.mp4' },
                              { kind: 'image', url: 'https://www.afisha.uz/media/show-2.jpg' },
                              { kind: 'image', url: 'javascript:alert(1)' },
                              { kind: 'image', url: 'https://www.afisha.uz/media/show-3.jpg' },
                            ],
                          },
                          links: [
                            {
                              id: 'link-1',
                              sourceId: 'source-1',
                              externalKey: 'show',
                              url: 'https://www.afisha.uz/ru/fashion/show',
                              state: 'live',
                              role: 'primary',
                            },
                          ],
                          fieldSources: {},
                          firstSeenAtUtc: '2026-11-10T08:11:00Z',
                          lastChangedAtUtc: '2026-11-10T08:11:00Z',
                        }
                      : path === '/api/posts/post-2'
                        ? {
                            id: 'post-2',
                            containerId: 'container-1',
                            state: 'live',
                            revision: 1,
                            base: {
                              schema: 'wow2.post.v1',
                              kind: 'article',
                              title: 'A guide to the season’s fabrics',
                              media: [],
                            },
                            links: [],
                            fieldSources: {},
                            firstSeenAtUtc: '2026-11-10T08:12:00Z',
                            lastChangedAtUtc: '2026-11-10T08:12:00Z',
                          }
                        : path === '/api/posts/post-1/revisions'
                          ? [
                              { revision: 2, at: '2026-11-10T08:11:00Z', changed: ['location'] },
                              { revision: 1, at: '2026-11-10T00:20:00Z', changed: ['created'] },
                            ]
                          : [];
    return Response.json({ data: body });
  });
}

interface Harness {
  readonly options?: HarvestConsoleOptions;
  readonly signInPath?: string;
  /** The panels the connected session shows. */
  readonly panels: () => VNodeChild;
}

/** Mounts a console session the way an app composes it: the connect card until connected, then the panels. */
function mountConsole({ options = {}, signInPath, panels }: Harness) {
  const Console = defineComponent({
    setup() {
      return () =>
        h(HarvestConsoleProvider, options, {
          default: ({ session }: { session: { connected: { value: boolean }; disconnect(): void } }) =>
            session.connected.value
              ? [h('button', { type: 'button', onClick: () => session.disconnect() }, 'Disconnect'), panels()]
              : h(HarvestConnectPanel, signInPath ? { signInPath } : {}),
        });
    },
  });
  const wrapper = mount(Console, { attachTo: document.body });
  wrappers.push(wrapper);
  return wrapper;
}

function button(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((item) => item.text() === text);
}

describe('SCFD console session', () => {
  it('offers Identity sign-in without requesting an integration key', async () => {
    const wrapper = mountConsole({
      options: { connectionManaged: true },
      signInPath: '/api/identity/sign-in?returnUrl=%2F',
      panels: () => h(HomePanel),
    });
    expect(wrapper.text()).toContain('Sign in to SCFD');
    expect(wrapper.get('a[href="/api/identity/sign-in?returnUrl=%2F"]').text()).toBe('Sign in');
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
  });

  it('stays disconnected until a key is submitted', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const wrapper = mountConsole({ panels: () => h(HomePanel) });
    await flushPromises();
    expect(fetch).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Connect to SCFD');
  });

  it('restores an opted-in tab after reload and forgets it on disconnect', async () => {
    vi.stubGlobal('fetch', fixtureFetch());
    const harness = { options: { connectionStorage: window.sessionStorage }, panels: () => h(SourcesGallery) };
    const first = mountConsole(harness);
    await first.get('input[type="password"]').setValue('hv_fixture');
    await first.get('form').trigger('submit');
    await flushPromises();
    expect(first.text()).toContain('Afisha.uz');
    wrappers.pop()?.unmount();

    const restored = mountConsole(harness);
    await flushPromises();
    expect(restored.text()).toContain('Afisha.uz');
    await button(restored, 'Disconnect')!.trigger('click');

    const disconnected = mountConsole(harness);
    await flushPromises();
    expect(disconnected.text()).toContain('Connect to SCFD');
  });

  it('ignores an old overview response after disconnect', async () => {
    let reply!: (value: ReturnType<typeof ResultExtensions.ok<readonly never[]>>) => void;
    const api = createHarvestClient({ fetch: fixtureFetch() });
    api.listSourceOverview = () =>
      new Promise((resolve) => {
        reply = resolve;
      });
    const wrapper = mountConsole({ options: { client: api }, panels: () => h(SourcesGallery) });
    await flushPromises();
    await button(wrapper, 'Disconnect')!.trigger('click');
    reply(ResultExtensions.ok([]));
    await flushPromises();
    expect(wrapper.text()).toContain('Connect to SCFD');
    expect(wrapper.find('[aria-label="Sources"]').exists()).toBe(false);
  });
});

describe('Sources gallery', () => {
  it('shows what the last harvest found on a harvested card, and no harvest yet on a never-harvested one', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcesGallery, { view: 'cards' }),
    });
    await flushPromises();
    const card = wrapper.get('article[aria-label="Afisha.uz"]');
    expect(card.get('[data-pill="last-harvest"]').text()).toContain('3 hours ago · +4 new · 23 found');
    expect(card.get('[data-pill="last-harvest"]').text()).toContain('partial');
    expect(card.get('[data-pill="last-harvest"]').attributes('data-tone')).toBe('warning');
    expect(card.text()).not.toContain('next');
    expect(card.text()).toContain('healthy');

    const never = wrapper.get('article[aria-label="Afisha Telegram"]');
    expect(never.get('[data-pill="last-harvest"]').text()).toBe('no harvest yet');
    expect(never.get('[data-pill="last-harvest"]').attributes('data-tone')).toBe('dashed');
    expect(never.get('[data-read-method]').attributes('data-read-method')).toBe('telegram');
  });

  it('shows the sources alone while one group holds them all: no group tile, no separator', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcesGallery, { view: 'cards' }),
    });
    await flushPromises();
    expect(wrapper.find('article[aria-label="Afisha"]').exists()).toBe(false);
    expect(wrapper.find('section header').exists()).toBe(false);
    expect(wrapper.find('article[aria-label="Afisha.uz"]').exists()).toBe(true);
    expect(wrapper.find('article[aria-label="Afisha Telegram"]').exists()).toBe(true);
  });

  it('draws a tile and a separator per group once there are two', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch({ groups: 2 }) }) },
      panels: () => h(SourcesGallery, { view: 'cards' }),
    });
    await flushPromises();
    const tile = wrapper.get('article[aria-label="Afisha"]');
    expect(tile.text()).toContain('1 source');
    expect(tile.get('[data-pill="group-harvest"]').text()).toBe('+4 new');
    expect(wrapper.findAll('section header')).toHaveLength(2);
  });

  it('filters by state with counts and searches by name', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcesGallery, { view: 'rows' }),
    });
    await flushPromises();
    const chips = wrapper
      .get('[aria-label="Filter by state"]')
      .findAll('button')
      .map((chip) => chip.text());
    expect(chips).toEqual(['All 2', 'Harvesting 1', 'Needs attention 0', 'Idle 1']);
    await wrapper.findAll('[aria-label="Filter by state"] button')[3]!.trigger('click');
    expect(wrapper.find('li[aria-label="Afisha.uz"]').exists()).toBe(false);
    expect(wrapper.find('li[aria-label="Afisha Telegram"]').exists()).toBe(true);
  });
});

describe('a source', () => {
  it('folds the sections nobody harvests into one closed line', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1' }),
    });
    await flushPromises();
    const harvestedTable = wrapper.get('[aria-label="Sections being harvested"]');
    expect(harvestedTable.text()).toContain('Fashion');
    expect(harvestedTable.text()).toContain('every 6 hours');
    expect(harvestedTable.text()).toContain('checks daily');
    expect(harvestedTable.text()).toContain('5-page limit');
    expect(harvestedTable.text()).toContain('1 subscriber');
    const fold = wrapper.get('details[data-folded-sections]');
    expect(fold.attributes('open')).toBeUndefined();
    expect(fold.get('summary').text()).toBe('2 more sections nobody follows');
    expect(fold.text()).toContain('Theatre');
    expect(fold.text()).toContain('Concerts');
    expect(wrapper.get('[aria-label="Health"]').text()).toContain('a page every 5 s');
  });

  it('splits a failed run into its kind as a label and its message as a sentence', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'activity' }),
    });
    await flushPromises();
    const problem = wrapper.get('[data-run-problem]');
    expect(problem.get('[data-problem-label]').text()).toBe('timed out');
    expect(problem.get('[data-problem-sentence]').text()).toBe('The host did not answer within 30 seconds.');
    expect(wrapper.get('table[aria-label="Runs"]').text()).toContain('Fashion');
  });

  it('turns a section on by creating its harvest from the section URL, never a typed one', async () => {
    const fetch = fixtureFetch();
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch }), isOperator: true },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'settings' }),
    });
    await flushPromises();
    const settings = wrapper.get('[aria-label="Sections"]');
    expect(settings.findAll('input[type="url"], input[placeholder*="URL" i]')).toHaveLength(0);
    await settings
      .get('[data-section="Theatre"] input[type="checkbox"], [data-section="Theatre"] [role="switch"]')
      .trigger('click');
    await flushPromises();
    const call = fetch.mock.calls.find(
      ([input, init]) =>
        new URL(String(input), 'https://harvest.example.test').pathname === '/api/pipelines' && init?.method === 'POST',
    );
    expect(call).toBeDefined();
    expect(JSON.parse(String(call![1]?.body))).toMatchObject({
      sourceId: 'source-1',
      name: 'Afisha.uz · Theatre',
      scope: { url: 'https://www.afisha.uz/ru/theatre', restrictToPath: true },
      schedules: [{ kind: 'capture', mode: 'interval', every: 'PT360M', enabled: true }],
    });
  });

  it('keeps Settings and Harvest now away from a session that does not operate SCFD', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }), isOperator: false },
      panels: () => h(SourcePanel, { sourceId: 'source-1' }),
    });
    await flushPromises();
    expect(wrapper.findAll('[role="tab"]').map((tab) => tab.text())).toEqual(['Overview', 'Posts 4', 'Activity 1']);
    expect(button(wrapper, 'Harvest now')).toBeUndefined();
  });

  it('queues one harvest for every harvested section', async () => {
    const fetch = fixtureFetch();
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch }), isOperator: true },
      panels: () => h(SourcePanel, { sourceId: 'source-1' }),
    });
    await flushPromises();
    await button(wrapper, 'Harvest now')!.trigger('click');
    await flushPromises();
    const runs = fetch.mock.calls.filter(
      ([input, init]) =>
        new URL(String(input), 'https://harvest.example.test').pathname === '/api/runs' && init?.method === 'POST',
    );
    expect(runs.map(([, init]) => JSON.parse(String(init?.body)))).toEqual([
      { pipelineId: 'pipeline-1', kind: 'capture' },
    ]);
  });

  it('opens a post on its own page with its revisions in words', async () => {
    mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(PostPanel, { sourceId: 'source-1', postId: 'post-1' }),
    });
    await flushPromises();
    const page = document.body.querySelector('[aria-label="Post"]');
    expect(page?.textContent).toContain('Starts');
    expect(page?.textContent).toContain('Revision 2');
    expect(page?.textContent).toContain('the place changed');
    expect(page?.textContent).toContain('first harvest');
  });
});

describe('post page and cards', () => {
  /** Mounts the page of the post with `postId` and returns the page's element. */
  async function openPost(postId: string) {
    mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(PostPanel, { sourceId: 'source-1', postId }),
    });
    await flushPromises();
    return document.body.querySelector('[aria-label="Post"]')!;
  }

  it('shows a post’s harvested images on its page, one large and the rest beside it', async () => {
    const page = await openPost('post-1');
    const images = [...page.querySelectorAll('[data-post-images] img')].map((image) => image.getAttribute('src'));
    expect(images).toEqual([
      'https://www.afisha.uz/media/show-1.jpg',
      'https://www.afisha.uz/media/show-2.jpg',
      'https://www.afisha.uz/media/show-3.jpg',
    ]);
    expect(page.querySelector('[data-post-images] img')?.getAttribute('referrerpolicy')).toBe('no-referrer');
    expect(page.querySelector('[data-post-images] a')).toBeNull();
  });

  it('shows no image block for a post without images', async () => {
    const page = await openPost('post-2');
    expect(page.textContent).toContain('Revision 1');
    expect(page.querySelector('[data-post-images]')).toBeNull();
    expect(page.querySelector('img')).toBeNull();
  });

  it('opens the original from the top right, or says why it cannot', async () => {
    const live = await openPost('post-1');
    const link = live.querySelector('header a[target="_blank"]');
    expect(link?.textContent).toContain('Open the original');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(live.querySelector('h1')?.textContent).toBe('Fashion Show Line 2026');
  });

  it('fills a post card with its first image, and gives a card without one a soft tile', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'posts', view: 'cards' }),
    });
    await flushPromises();
    const cards = wrapper.findAll('ul[aria-label="Posts"] > li');
    expect(cards).toHaveLength(2);
    expect(cards[0]!.get('[data-post-image]').attributes('src')).toBe('https://www.afisha.uz/media/show-1.jpg');
    expect(cards[1]!.find('img').exists()).toBe(false);
  });

  it('gives a card with several images a two-arrow carousel, and leaves a card without them plain', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'posts', view: 'cards' }),
    });
    await flushPromises();
    const [several, none] = wrapper.findAll('ul[aria-label="Posts"] > li');
    expect(several!.findAll('[data-post-image]').map((image) => image.attributes('src'))).toEqual([
      'https://www.afisha.uz/media/show-1.jpg',
      'https://www.afisha.uz/media/show-2.jpg',
    ]);
    const track = several!.get('[aria-live]');
    expect(track.attributes('style')).toContain('translateX(-0%)');
    await several!.get('button[aria-label="Next slide"]').trigger('click');
    expect(track.attributes('style')).toContain('translateX(-100%)');
    await several!.get('button[aria-label="Previous slide"]').trigger('click');
    expect(track.attributes('style')).toContain('translateX(-0%)');
    expect(none!.find('button[aria-label="Next slide"]').exists()).toBe(false);
  });

  it('shows a one-image card the same two arrows, disabled', async () => {
    const base = fixtureFetch();
    const single = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const response = await base(input, init);
      const path = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test')
        .pathname;
      if (path !== '/api/sources/source-1/posts') return response;
      const body = (await response.json()) as { data: { imageUrl?: string; imageUrls?: string[] }[] };
      body.data[0]!.imageUrls = [body.data[0]!.imageUrl!];
      return Response.json(body);
    });
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: single }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'posts', view: 'cards' }),
    });
    await flushPromises();
    const [card] = wrapper.findAll('ul[aria-label="Posts"] > li');
    expect(card!.findAll('[data-post-image]')).toHaveLength(1);
    expect(card!.get('button[aria-label="Previous slide"]').attributes('disabled')).toBeDefined();
    expect(card!.get('button[aria-label="Next slide"]').attributes('disabled')).toBeDefined();
  });

  it('writes a card’s pills in lower case, and never says when SCFD saw the post', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'posts', view: 'cards' }),
    });
    await flushPromises();
    for (const card of wrapper.findAll('ul[aria-label="Posts"] > li')) {
      const pills = card.findAll('[data-tone]').map((pill) => pill.text());
      expect(pills.length).toBeGreaterThan(0);
      for (const text of pills) expect(text).toBe(text.toLowerCase());
      expect(card.text()).not.toContain('seen');
    }
  });

  it('puts an open-original button on a card with a link, and a disabled one on a card without', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(SourcePanel, { sourceId: 'source-1', tab: 'posts', view: 'cards' }),
    });
    await flushPromises();
    const [withLink, without] = wrapper.findAll('ul[aria-label="Posts"] > li');
    const open = withLink!.get('a[target="_blank"]');
    expect(open.attributes('href')).toBe('https://www.afisha.uz/ru/fashion/show');
    expect(open.attributes('rel')).toBe('noopener noreferrer');
    expect(without!.find('a[target="_blank"]').exists()).toBe(false);
    expect(without!.get('[role="img"]').attributes('aria-label')).toBe('No link to the original');
  });
});

describe('access and home', () => {
  it('names the session’s permissions in words, never scope names', async () => {
    const wrapper = mountConsole({
      options: {
        client: createHarvestClient({ fetch: fixtureFetch() }),
        permissions: ['pipelines:read', 'posts:read'],
      },
      panels: () => h(AccessPanel),
    });
    await flushPromises();
    expect(wrapper.get('[aria-label="Allowed"]').text()).toContain('Read harvested posts');
    expect(wrapper.get('[aria-label="Not allowed"]').text()).toContain('Start and cancel harvests');
    expect(wrapper.text()).not.toMatch(/\w+:(read|run|manage)/);
    expect(wrapper.text().toLowerCase()).not.toContain('container');
    expect(wrapper.text().toLowerCase()).not.toContain('tenant');
  });

  it('reports the workspace health on the home page', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(HomePanel),
    });
    await flushPromises();
    const health = wrapper.get('[aria-label="Health"]');
    expect(health.text()).toContain('Failed harvests');
    expect(health.text()).toContain('Delivery lag');
    expect(wrapper.get('[aria-label="Latest harvests"]').text()).toContain('Afisha.uz · Fashion');
  });
});
