import { flushPromises, mount } from '@vue/test-utils';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, type VNodeChild } from 'vue';
import HarvestConnectPanel from '../src/admin/harvestConsole/HarvestConnectPanel.vue';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import HomePanel from '../src/admin/harvestConsole/HomePanel.vue';
import PipelinePanel from '../src/admin/harvestConsole/PipelinePanel.vue';
import PipelinesPanel from '../src/admin/harvestConsole/PipelinesPanel.vue';
import PostsPanel from '../src/admin/harvestConsole/PostsPanel.vue';
import SourcesPanel from '../src/admin/harvestConsole/SourcesPanel.vue';
import type { ConsoleNavigation, HarvestConsoleOptions } from '../src/admin';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  window.sessionStorage.clear();
  vi.unstubAllGlobals();
});

const sections = [
  { label: 'Concerts', url: 'https://mstar.uz/concerts' },
  { label: 'Theatre', url: 'https://mstar.uz/theatre' },
];

/** Replies as a small complete service, with no real network. */
function fixtureFetch({ withSections = false } = {}) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
    const method = input instanceof Request ? input.method : (init?.method ?? 'GET');
    if (url.pathname.endsWith('/capabilities'))
      return Response.json({
        data: {
          sourceId: 'source-1',
          adapterKey: 'fixture',
          workKinds: ['capture', 'check'],
          ...(withSections ? { supportsPathScope: true } : {}),
          configuration: {
            displayName: 'Fixture source',
            readiness: 'ready',
            scopeSchema: withSections ? { sections } : {},
          },
        },
      });
    if (url.pathname === '/api/pipelines' && method === 'POST')
      return Response.json({
        data: { id: 'pipeline-2', name: 'New', state: 'active', sourceId: 'source-1', scope: {}, schedules: [] },
      });
    const body =
      url.pathname === '/api/containers'
        ? [{ id: 'container-1', kind: 'pool' }]
        : url.pathname === '/api/sources'
          ? [{ id: 'source-1', handle: 'mstar.uz', family: 'site', state: 'active', coverage: 'whole' }]
          : url.pathname === '/api/pipelines'
            ? [
                {
                  id: 'pipeline-1',
                  name: 'Upcoming events',
                  state: 'active',
                  sourceId: 'source-1',
                  scope: {},
                  schedules: [],
                },
              ]
            : url.pathname === '/api/runs' && method === 'POST'
              ? { id: 'run-1', status: 'queued' }
              : url.pathname === '/api/posts'
                ? [
                    {
                      id: 'post-1',
                      containerId: 'container-1',
                      state: 'live',
                      revision: 1,
                      base: {
                        schema: 'wow2.post.v1',
                        kind: 'event',
                        title: 'Fixture festival',
                        text: 'Meet the makers in Samarqand.',
                      },
                      links: [{ id: 'link-1', sourceId: 'source-1', url: 'https://mstar.uz/festival', state: 'live' }],
                      fieldSources: { text: 'link-1' },
                    },
                  ]
                : url.pathname === '/api/runs'
                  ? [
                      {
                        id: 'run-1',
                        kind: 'capture',
                        status: 'partial',
                        captured: 0,
                        unchanged: 0,
                        discovered: 0,
                        failed: 1,
                        fetchOutcome: 'challenge',
                        queuedAtUtc: '2026-10-27T09:00:00Z',
                      },
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
          default: ({
            session,
          }: {
            session: { connected: { value: boolean }; disconnect(): void; refresh(): void };
          }) =>
            session.connected.value
              ? [
                  h('button', { type: 'button', onClick: () => session.disconnect() }, 'Disconnect'),
                  h('button', { type: 'button', onClick: () => session.refresh() }, 'Refresh'),
                  panels(),
                ]
              : h(HarvestConnectPanel, signInPath ? { signInPath } : {}),
        });
    },
  });
  const wrapper = mount(Console);
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

  it('keeps pipeline changes and manual runs out of a read-only session', async () => {
    const wrapper = mountConsole({
      options: {
        client: createHarvestClient({ fetch: fixtureFetch() }),
        connectionManaged: true,
        permissions: ['pipelines:read', 'posts:read'],
      },
      panels: () => [h(PipelinesPanel), h(PipelinePanel, { pipelineId: 'pipeline-1' })],
    });
    await flushPromises();
    expect(wrapper.get('[aria-label="Pipelines"]').text()).toContain('Upcoming events');
    expect(wrapper.get('[aria-label="Pipeline overview"]').text()).toContain('mstar.uz');
    expect(wrapper.findAll('button').some((item) => ['New pipeline', 'Run now', 'Pause'].includes(item.text()))).toBe(
      false,
    );
    expect(wrapper.find('form[aria-label="Pipeline settings"]').exists()).toBe(false);
    expect(wrapper.find('form[aria-label="New pipeline"]').exists()).toBe(false);
  });

  it('stays disconnected until a key is submitted', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const wrapper = mountConsole({ panels: () => h(HomePanel) });
    await flushPromises();
    expect(fetch).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Connect to SCFD');
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
  });

  it('restores an opted-in tab after reload and forgets it on disconnect', async () => {
    vi.stubGlobal('fetch', fixtureFetch());
    const harness = { options: { connectionStorage: window.sessionStorage }, panels: () => h(PipelinesPanel) };
    const first = mountConsole(harness);
    await first.get('input[type="password"]').setValue('hv_fixture');
    await first.get('form').trigger('submit');
    await flushPromises();
    expect(first.text()).toContain('Upcoming events');
    wrappers.pop()?.unmount();

    const restored = mountConsole(harness);
    await flushPromises();
    expect(restored.text()).toContain('Upcoming events');
    await button(restored, 'Disconnect')!.trigger('click');

    const disconnected = mountConsole(harness);
    await flushPromises();
    expect(disconnected.text()).toContain('Connect to SCFD');
  });

  it('explains an empty source catalog without waiting for capabilities', async () => {
    const api = createHarvestClient({ fetch: fixtureFetch() });
    api.listSources = async () => ResultExtensions.ok([]);
    api.listPipelines = async () => ResultExtensions.ok([]);
    const loadCapabilities = vi.spyOn(api, 'getSourceCapabilities');
    const wrapper = mountConsole({ options: { client: api }, panels: () => [h(PipelinesPanel), h(SourcesPanel)] });
    await flushPromises();

    expect(wrapper.text()).toContain('No sources registered in this container.');
    expect(wrapper.text()).toContain('No sources in this container');
    expect(wrapper.find('[aria-label="Source capabilities"]').exists()).toBe(false);
    expect(loadCapabilities).not.toHaveBeenCalled();
  });

  it('shows the source catalog read-only, with each adapter and its sections', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch({ withSections: true }) }) },
      panels: () => h(SourcesPanel),
    });
    await flushPromises();
    const catalog = wrapper.get('[aria-label="Sources"]');
    expect(catalog.text()).toContain('mstar.uz');
    expect(catalog.text()).toContain('Fixture source');
    expect(catalog.get('[aria-label="Sections"]').text()).toContain('Theatre');
    expect(catalog.find('form').exists()).toBe(false);
    expect(catalog.find('input').exists()).toBe(false);
  });

  it('creates a pipeline from a chosen source and section, never a typed URL', async () => {
    const fetch = fixtureFetch({ withSections: true });
    const navigation: ConsoleNavigation = { href: () => '/pipelines', go: vi.fn() };
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch }), navigation },
      panels: () => h(PipelinesPanel, { newSource: 'source-1' }),
    });
    await flushPromises();
    const form = wrapper.get('form[aria-label="New pipeline"]');
    expect(form.findAll('input').some((input) => /url/i.test(input.attributes('placeholder') ?? ''))).toBe(false);
    await form
      .findAll('button')
      .find((item) => item.text() === 'Theatre')!
      .trigger('click');
    await form.trigger('submit');
    await flushPromises();

    const call = fetch.mock.calls.find(
      ([input, init]) =>
        new URL(String(input), 'https://harvest.example.test').pathname === '/api/pipelines' && init?.method === 'POST',
    )!;
    const body = JSON.parse(String(call[1]?.body)) as Record<string, unknown>;
    expect(body).toMatchObject({
      sourceId: 'source-1',
      name: 'mstar.uz · Theatre',
      scope: { url: 'https://mstar.uz/theatre', restrictToPath: true },
    });
    expect(navigation.go).toHaveBeenCalledWith({ kind: 'pipeline', id: 'pipeline-2' });
  });

  it('queues a run and shows a challenge outcome on the pipeline page', async () => {
    const fetch = fixtureFetch();
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ apiKey: 'hv_fixture', fetch }) },
      panels: () => h(PipelinePanel, { pipelineId: 'pipeline-1' }),
    });
    await flushPromises();
    expect(wrapper.get('[aria-label="Pipeline overview"]').text()).toContain('mstar.uz');
    expect(wrapper.find('section[aria-label="Configuration"]').exists()).toBe(true);
    expect(wrapper.find('section[aria-label="Schedule"]').exists()).toBe(true);
    await button(wrapper, 'Run now')!.trigger('click');
    await flushPromises();
    expect(
      fetch.mock.calls.some(
        ([input, init]) =>
          new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test').pathname ===
            '/api/runs' && (input instanceof Request ? input.method : init?.method) === 'POST',
      ),
    ).toBe(true);
    expect(wrapper.get('[aria-label="Runs"]').text()).toContain('challenge');
    await button(wrapper, 'Disconnect')!.trigger('click');
    expect(wrapper.text()).toContain('Connect to SCFD');
    expect(wrapper.find('[aria-label="Pipeline overview"]').exists()).toBe(false);
  });

  it('ignores an old connection response after disconnect', async () => {
    let reply!: (value: ReturnType<typeof ResultExtensions.ok<readonly never[]>>) => void;
    const api = createHarvestClient({ fetch: fixtureFetch() });
    api.listPosts = () =>
      new Promise((resolve) => {
        reply = resolve;
      });
    const wrapper = mountConsole({ options: { client: api }, panels: () => h(PipelinesPanel) });
    await flushPromises();
    await button(wrapper, 'Disconnect')!.trigger('click');
    reply(ResultExtensions.ok([]));
    await flushPromises();
    expect(wrapper.text()).toContain('Connect to SCFD');
    expect(wrapper.find('[aria-label="Pipelines"]').exists()).toBe(false);
  });

  it('refreshes the selected post when new content or availability evidence arrives', async () => {
    const api = createHarvestClient({ fetch: fixtureFetch() });
    const loadPosts = api.listPosts;
    let changed = false;
    api.listPosts = async (containerId, signal) => {
      const result = await loadPosts(containerId, signal);
      if (!result.ok || !changed) return result;
      return ResultExtensions.ok(
        result.value.map((post) => ({
          ...post,
          base: { ...post.base, text: 'Updated source body.' },
          links: post.links.map((link) => ({ ...link, lastCheckOutcome: 'Present', lastCheckHttpStatus: 304 })),
        })),
      );
    };
    const wrapper = mountConsole({ options: { client: api }, panels: () => h(PostsPanel) });
    await flushPromises();
    await wrapper.get('[aria-label="Posts"] button').trigger('click');
    changed = true;
    await button(wrapper, 'Refresh')!.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Updated source body.');
    expect(wrapper.text()).toContain('Present · HTTP 304');
  });

  it('renders the backend post text with its supporting link', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(PostsPanel),
    });
    await flushPromises();
    await wrapper.get('[aria-label="Posts"] button').trigger('click');
    expect(wrapper.text()).toContain('Meet the makers in Samarqand.');
    expect(wrapper.get('a[href="https://mstar.uz/festival"]').attributes('rel')).toBe('noopener noreferrer');
  });

  it('reports the container health on the home page', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: fixtureFetch() }) },
      panels: () => h(HomePanel),
    });
    await flushPromises();
    const health = wrapper.get('[aria-label="Health"]');
    expect(health.text()).toContain('Failed runs');
    expect(health.text()).toContain('Delivery lag');
    expect(wrapper.get('[aria-label="Runs"]').text()).toContain('Upcoming events');
  });
});
