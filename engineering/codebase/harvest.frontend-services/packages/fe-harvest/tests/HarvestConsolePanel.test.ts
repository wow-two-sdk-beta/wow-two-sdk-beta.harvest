import { flushPromises, mount } from '@vue/test-utils';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { afterEach, describe, expect, it, vi } from 'vitest';
import HarvestConsolePanel from '../src/admin/harvestConsole/HarvestConsolePanel.vue';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.unstubAllGlobals();
});

/** Replies as a small complete service, with no real network. */
function fixtureFetch() {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
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
            : url.pathname === '/api/runs' && init?.method === 'POST'
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

describe('HarvestConsolePanel', () => {
  it('stays disconnected until a key is submitted', async () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    const wrapper = mount(HarvestConsolePanel);
    wrappers.push(wrapper);
    await flushPromises();
    expect(fetch).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Connect to Harvest');
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
  });

  it('queues a run and shows a challenge outcome through reusable panels', async () => {
    const fetch = fixtureFetch();
    const wrapper = mount(HarvestConsolePanel, {
      props: { client: createHarvestClient({ apiKey: 'hv_fixture', fetch }) },
    });
    wrappers.push(wrapper);
    await flushPromises();
    expect(wrapper.get('[aria-label="Pipelines"]').text()).toContain('Upcoming events');
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Run now')!
      .trigger('click');
    await flushPromises();
    expect(
      fetch.mock.calls.some(
        ([input, init]) =>
          new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test').pathname ===
            '/api/runs' && (input instanceof Request ? input.method : init?.method) === 'POST',
      ),
    ).toBe(true);
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Runs')!
      .trigger('click');
    expect(wrapper.get('[aria-label="Runs"]').text()).toContain('challenge');
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Disconnect')!
      .trigger('click');
    expect(wrapper.text()).toContain('Connect to Harvest');
    expect(wrapper.text()).not.toContain('Upcoming events');
  });

  it('ignores an old connection response after disconnect', async () => {
    let reply!: (value: ReturnType<typeof ResultExtensions.ok<readonly never[]>>) => void;
    const api = createHarvestClient({ fetch: fixtureFetch() });
    api.listPosts = () =>
      new Promise((resolve) => {
        reply = resolve;
      });
    const wrapper = mount(HarvestConsolePanel, { props: { client: api } });
    wrappers.push(wrapper);
    await flushPromises();
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Disconnect')!
      .trigger('click');
    reply(ResultExtensions.ok([]));
    await flushPromises();
    expect(wrapper.text()).toContain('Connect to Harvest');
    expect(wrapper.find('[aria-label="Pipelines"]').exists()).toBe(false);
  });

  it('renders the backend post text with its supporting link', async () => {
    const wrapper = mount(HarvestConsolePanel, { props: { client: createHarvestClient({ fetch: fixtureFetch() }) } });
    wrappers.push(wrapper);
    await flushPromises();
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Posts')!
      .trigger('click');
    await wrapper.get('[aria-label="Posts"] button').trigger('click');
    expect(wrapper.text()).toContain('Meet the makers in Samarqand.');
    expect(wrapper.get('a[href="https://mstar.uz/festival"]').attributes('rel')).toBe('noopener noreferrer');
  });
});
