import { flushPromises, mount } from '@vue/test-utils';
import { createFeedbackBus, NoticeTone } from '@wow-two-beta/fe/feedback';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, type VNodeChild } from 'vue';
import AvailableSources from '../src/admin/harvestConsole/AvailableSources.vue';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import SourcePanel from '../src/admin/harvestConsole/SourcePanel.vue';
import type { ConsoleNavigation, HarvestConsole, HarvestConsoleOptions } from '../src/admin';
import type { CatalogSourceModel } from '../src/contracts';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
/** The fixture's Kultura entry was turned off on 8 Nov 2026; the clock stays near it. */
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-11-10T12:00:00Z'));
});
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  document.body.innerHTML = '';
  vi.useRealTimers();
});

const afisha: CatalogSourceModel = {
  key: 'ocharo.afisha',
  catalog: 'ocharo',
  name: 'Afisha',
  description: 'Event listings across Uzbekistan.',
  kind: 'events',
  hosts: [
    { family: 'site', handle: 'www.afisha.uz' },
    { family: 'telegram', handle: 'afisha_uz' },
  ],
  channels: 6,
  state: 'on',
  changedAtUtc: '2026-10-01T09:00:00Z',
  changedBy: 'existing',
  sources: [
    { sourceId: 'source-1', containerId: 'container-1', name: 'Afisha.uz' },
    { sourceId: 'source-2', containerId: 'container-1', name: 'Afisha Telegram' },
  ],
};
const kultura: CatalogSourceModel = {
  key: 'ocharo.kultura',
  catalog: 'ocharo',
  name: 'Kultura',
  kind: 'events',
  hosts: [{ family: 'site', handle: 'kultura.uz' }],
  channels: 2,
  state: 'off',
  changedAtUtc: '2026-11-08T12:00:00Z',
  changedBy: 'operator-key',
  sources: [{ sourceId: 'source-3', containerId: 'container-1', name: 'Kultura.uz' }],
};
const philharmonic: CatalogSourceModel = {
  key: 'ocharo.city-philharmonic',
  catalog: 'ocharo',
  name: 'City Philharmonic',
  kind: 'events',
  hosts: [{ family: 'site', handle: 'city-philharmonic.example' }],
  channels: 1,
  state: 'new',
  sources: [],
};
const birbir: CatalogSourceModel = {
  key: 'marketplace.birbir-uz',
  catalog: 'marketplace',
  name: 'BirBir',
  description: 'Classified listings.',
  kind: 'listings',
  hosts: [{ family: 'listing', handle: 'birbir.uz' }],
  channels: 3,
  state: 'new',
  sources: [],
};

/** Afisha.uz, set up by its catalog entry. */
const overview = (sourceId: string, name: string, catalogKey?: string) => ({
  sourceId,
  name,
  family: 'site',
  handle: `${sourceId}.example`,
  ...(catalogKey ? { catalogKey } : {}),
  state: 'idle',
  sourceState: 'active',
  newPostsDaily: Array.from({ length: 14 }, () => 0),
  sections: [],
  health: { hostKey: 'www.afisha.uz', minDelayMs: 5000, consecutiveFailures: 0 },
});

interface Call {
  readonly method: string;
  readonly path: string;
  readonly body?: unknown;
}

interface Service {
  readonly fetch: ReturnType<typeof vi.fn<typeof globalThis.fetch>>;
  readonly calls: Call[];
  /** The catalog as the service holds it now. */
  catalog: CatalogSourceModel[];
  /** Answers the next catalog read with this status instead of the list. */
  failRead?: number;
  /** Answers every switch with this status and reason. */
  failSwitch?: { status: number; title: string };
}

/** Replies as a small stateful service: one open container, two catalog sources and a switchable catalog. */
function service(catalog: CatalogSourceModel[] = [afisha, kultura, philharmonic, birbir]): Service {
  const calls: Call[] = [];
  const state: Service = {
    calls,
    catalog,
    fetch: vi.fn<typeof globalThis.fetch>(async (input, init) => {
      const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
      const method = input instanceof Request ? input.method : (init?.method ?? 'GET');
      const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined;
      calls.push({ method, path: url.pathname, ...(body !== undefined ? { body } : {}) });
      const path = url.pathname;
      if (path === '/api/sources/catalog' && method === 'GET') {
        if (state.failRead) return Response.json({ title: 'Down' }, { status: state.failRead });
        return Response.json({ data: state.catalog });
      }
      if (path.startsWith('/api/sources/catalog/') && method === 'PUT') {
        if (state.failSwitch)
          return Response.json({ title: state.failSwitch.title }, { status: state.failSwitch.status });
        const key = decodeURIComponent(path.slice('/api/sources/catalog/'.length));
        const turnedOn = (body as { turnedOn: boolean }).turnedOn;
        const entry = state.catalog.find((item) => item.key === key);
        if (!entry) return Response.json({ title: 'Unknown' }, { status: 404 });
        const next: CatalogSourceModel = {
          ...entry,
          state: turnedOn ? 'on' : 'off',
          changedAtUtc: '2026-11-10T12:00:00Z',
          changedBy: 'operator-key',
        };
        state.catalog = state.catalog.map((item) => (item.key === key ? next : item));
        return Response.json({ data: next });
      }
      if (path.endsWith('/capabilities'))
        return Response.json({
          data: {
            sourceId: 'source-1',
            workKinds: ['capture'],
            configuration: { displayName: 'Afisha', readiness: 'ready', scopeSchema: { sections: [] } },
          },
        });
      const data =
        path === '/api/containers'
          ? [{ id: 'container-1', kind: 'pool' }]
          : path === '/api/sources'
            ? [
                {
                  id: 'source-1',
                  handle: 'www.afisha.uz',
                  family: 'site',
                  state: 'active',
                  permission: 'open',
                  catalogKey: 'ocharo.afisha',
                  turnedOn: true,
                },
                {
                  id: 'source-2',
                  handle: 'afisha_uz',
                  family: 'telegram',
                  state: 'active',
                  permission: 'open',
                  catalogKey: 'ocharo.afisha',
                  turnedOn: true,
                },
                {
                  id: 'source-3',
                  handle: 'kultura.uz',
                  name: 'Kultura.uz',
                  family: 'site',
                  state: 'active',
                  permission: 'open',
                  catalogKey: 'ocharo.kultura',
                  turnedOn: false,
                },
                {
                  id: 'source-4',
                  handle: 'by-hand.example',
                  family: 'site',
                  state: 'active',
                  permission: 'open',
                  turnedOn: true,
                },
              ]
            : path === '/api/sources/overview'
              ? [
                  overview('source-1', 'Afisha.uz', 'ocharo.afisha'),
                  overview('source-2', 'Afisha Telegram', 'ocharo.afisha'),
                  overview('source-4', 'By hand'),
                ]
              : [];
      return Response.json({ data });
    }),
  };
  return state;
}

interface Harness {
  readonly options?: HarvestConsoleOptions;
  readonly panels: (session: HarvestConsole) => VNodeChild;
}

/** Mounts a connected console session around the panels, the way an app composes it. */
function mountConsole({ options = {}, panels }: Harness) {
  const Console = defineComponent({
    setup() {
      return () =>
        h(
          HarvestConsoleProvider,
          { refreshIntervalMs: 60_000, ...options },
          {
            default: ({ session }: { session: HarvestConsole }) => (session.connected.value ? panels(session) : null),
          },
        );
    },
  });
  const wrapper = mount(Console, { attachTo: document.body });
  wrappers.push(wrapper);
  return wrapper;
}

/** The catalog reads and the switches the service saw, in order. */
const catalogCalls = (calls: readonly Call[]) =>
  calls.filter((call) => call.path.startsWith('/api/sources/catalog')).map((call) => `${call.method} ${call.path}`);

function button(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((item) => item.text() === text);
}

describe('Available sources', () => {
  it('lists the entries that are new or turned off, under their kind, and leaves the turned-on ones out', async () => {
    const api = service();
    const wrapper = mountConsole({
      options: {
        client: createHarvestClient({ fetch: api.fetch }),
        navigation: { href: (place) => `/#${place.kind}`, go: vi.fn() },
      },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    expect(wrapper.text()).toContain('Registered sources this instance has not turned on.');
    expect(wrapper.findAll('section h2').map((heading) => heading.text())).toEqual(['Events', 'Listings']);
    expect(wrapper.findAll('article').map((card) => card.attributes('aria-label'))).toEqual([
      'City Philharmonic',
      'Kultura',
      'BirBir',
    ]);
    expect(wrapper.find('article[aria-label="Afisha"]').exists()).toBe(false);

    const fresh = wrapper.get('article[aria-label="BirBir"]');
    expect(fresh.text()).toContain('Listings');
    expect(fresh.text()).toContain('birbir.uz');
    expect(fresh.text()).toContain('3 channels');
    expect(fresh.text()).not.toContain('Turned off');
    expect(fresh.find('a').exists()).toBe(false);
    expect(fresh.get('[data-read-method]').attributes('data-read-method')).toBe('site');

    const off = wrapper.get('article[aria-label="Kultura"]');
    expect(off.text()).toContain('Events');
    expect(off.text()).toContain('2 channels');
    expect(off.get('[data-tone="warning"]').text()).toBe('Turned off 8 Nov 2026');
    expect(off.get('a').text()).toBe('Kultura.uz');
    expect(off.get('a').attributes('href')).toBe('/#source');
  });

  it('shows the same entries as rows through the same view prop', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: service().fetch }) },
      panels: () => h(AvailableSources, { view: 'rows' }),
    });
    await flushPromises();
    expect(wrapper.find('article').exists()).toBe(false);
    expect(wrapper.findAll('li[aria-label]').map((row) => row.attributes('aria-label'))).toEqual([
      'City Philharmonic',
      'Kultura',
      'BirBir',
    ]);
    expect(wrapper.get('li[aria-label="Kultura"]').text()).toContain('Turned off 8 Nov 2026');
  });

  it('turns an entry on with a PUT, reads the catalog again and drops the entry from the list', async () => {
    const api = service();
    const feedback = createFeedbackBus();
    const notices = vi.fn();
    feedback.subscribe(notices);
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }), feedback },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    const before = catalogCalls(api.calls).filter((call) => call === 'GET /api/sources/catalog').length;

    await wrapper.get('button[aria-label="Turn on BirBir"]').trigger('click');
    await flushPromises();

    const put = api.calls.find((call) => call.method === 'PUT');
    expect(put).toEqual({
      method: 'PUT',
      path: '/api/sources/catalog/marketplace.birbir-uz',
      body: { turnedOn: true },
    });
    const after = catalogCalls(api.calls);
    expect(after.filter((call) => call === 'GET /api/sources/catalog').length).toBeGreaterThan(before);
    expect(after.indexOf('PUT /api/sources/catalog/marketplace.birbir-uz')).toBeLessThan(
      after.lastIndexOf('GET /api/sources/catalog'),
    );
    expect(wrapper.find('article[aria-label="BirBir"]').exists()).toBe(false);
    expect(wrapper.find('section h2').text()).toBe('Events');
    expect(notices).toHaveBeenCalledWith(
      expect.objectContaining({ tone: NoticeTone.Success, title: 'BirBir is turned on.' }),
    );
  });

  it('keeps the entry and reports the reason when the set-up fails', async () => {
    const api = service();
    api.failSwitch = { status: 409, title: 'Setting up BirBir failed' };
    const feedback = createFeedbackBus();
    const notices = vi.fn();
    feedback.subscribe(notices);
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }), feedback },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    await wrapper.get('button[aria-label="Turn on BirBir"]').trigger('click');
    await flushPromises();
    expect(notices).toHaveBeenCalledWith(expect.objectContaining({ tone: NoticeTone.Danger }));
    expect(notices).not.toHaveBeenCalledWith(expect.objectContaining({ tone: NoticeTone.Success }));
    expect(wrapper.find('article[aria-label="BirBir"]').exists()).toBe(true);
    expect(wrapper.get('button[aria-label="Turn on BirBir"]').attributes('disabled')).toBeUndefined();
  });

  it('disables Turn on, with the reason, for a session that cannot manage sources', async () => {
    const api = service();
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }), isOperator: true, permissions: ['sources:read'] },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    const turnOn = wrapper.get('button[aria-label="Turn on BirBir"]');
    expect(turnOn.attributes('disabled')).toBeDefined();
    await turnOn.trigger('click');
    await flushPromises();
    expect(api.calls.some((call) => call.method === 'PUT')).toBe(false);
  });

  it('shows a tenant session no catalog, and never reads it', async () => {
    const api = service();
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }), isOperator: false },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    expect(wrapper.text()).toContain('Available is for operators');
    expect(wrapper.find('article').exists()).toBe(false);
    expect(catalogCalls(api.calls)).toEqual([]);
  });

  it('says every registered source is turned on when none is left', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: service([afisha]).fetch }) },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    expect(wrapper.text()).toContain('Every registered source is turned on.');
    expect(wrapper.find('article').exists()).toBe(false);
  });

  it('searches the name, the key and the addresses, and clears a search that matches nothing', async () => {
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: service().fetch }) },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    const search = wrapper.get('input[aria-label="Search available sources"]');
    const names = () => wrapper.findAll('article').map((card) => card.attributes('aria-label'));

    await search.setValue('birbir.uz');
    expect(names()).toEqual(['BirBir']);
    await search.setValue('OCHARO.CITY');
    expect(names()).toEqual(['City Philharmonic']);
    await search.setValue('kultura');
    expect(names()).toEqual(['Kultura']);

    await search.setValue('nothing like this');
    expect(wrapper.text()).toContain('No source matches');
    await button(wrapper, 'Clear search')!.trigger('click');
    expect(names()).toHaveLength(3);
  });

  it('says the list is unavailable, with Retry, instead of an empty one', async () => {
    const api = service();
    api.failRead = 500;
    const wrapper = mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }) },
      panels: () => h(AvailableSources),
    });
    await flushPromises();
    expect(wrapper.text()).toContain('Could not load the available sources.');
    expect(wrapper.text()).not.toContain('Every registered source is turned on.');

    delete api.failRead;
    await button(wrapper, 'Retry')!.trigger('click');
    await flushPromises();
    expect(wrapper.text()).not.toContain('Could not load');
    expect(wrapper.findAll('article')).toHaveLength(3);
  });

  it('counts the entries still to turn on for the sidebar, reading the catalog once outside the poll', async () => {
    const api = service();
    let count = -1;
    mountConsole({
      options: { client: createHarvestClient({ fetch: api.fetch }), refreshIntervalMs: 20 },
      panels: (session) => {
        count = session.availableCount.value;
        return h('p', String(session.availableCount.value));
      },
    });
    vi.useRealTimers();
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 90));
    await flushPromises();
    expect(count).toBe(3);
    expect(catalogCalls(api.calls)).toEqual(['GET /api/sources/catalog']);
    expect(api.calls.filter((call) => call.path === '/api/sources/overview').length).toBeGreaterThan(1);
  });
});

/** Mounts one source's Settings with a navigation that records where the console goes. */
function mountSettings(sourceId: string, options: HarvestConsoleOptions = {}) {
  const api = service();
  const go = vi.fn<ConsoleNavigation['go']>();
  const wrapper = mountConsole({
    options: { client: createHarvestClient({ fetch: api.fetch }), navigation: { href: () => '/x', go }, ...options },
    panels: () => h(SourcePanel, { sourceId, tab: 'settings' }),
  });
  return { api, go, wrapper };
}

const dialog = () => document.body.querySelector('[role="alertdialog"]');
const dialogButton = (text: string) =>
  [...(dialog()?.querySelectorAll('button') ?? [])].find((item) => item.textContent?.trim() === text);

describe('Turning a source off from its Settings', () => {
  it('reads "Turned on for this instance" with Turn off for a catalog source', async () => {
    const { wrapper } = mountSettings('source-1', { isOperator: true });
    await flushPromises();
    expect(wrapper.text()).toContain('Instance');
    expect(wrapper.text()).toContain('Turned on for this instance');
    expect(button(wrapper, 'Turn off')).toBeDefined();
  });

  it('shows no instance row for a source registered by hand', async () => {
    const { wrapper } = mountSettings('source-4', { isOperator: true });
    await flushPromises();
    expect(wrapper.find('[aria-label="Reading"]').exists()).toBe(true);
    expect(wrapper.text()).not.toContain('Turned on for this instance');
    expect(button(wrapper, 'Turn off')).toBeUndefined();
  });

  it('asks first, naming the entry and every source it covers, and changes nothing on Cancel', async () => {
    const { api, go, wrapper } = mountSettings('source-1', { isOperator: true });
    await flushPromises();
    await button(wrapper, 'Turn off')!.trigger('click');
    await flushPromises();

    expect(dialog()).not.toBeNull();
    expect(dialog()!.textContent).toContain('Turn off Afisha?');
    expect(dialog()!.textContent).toContain('stops collecting, processing and delivering on this instance');
    expect(dialog()!.textContent).toContain('posts, runs and settings stay');
    expect(dialog()!.textContent).toContain('Available can turn it back on');
    expect([...dialog()!.querySelectorAll('li')].map((item) => item.textContent)).toEqual([
      'Afisha.uz',
      'Afisha Telegram',
    ]);
    expect(api.calls.some((call) => call.method === 'PUT')).toBe(false);

    dialogButton('Cancel')!.click();
    await flushPromises();
    expect(api.calls.some((call) => call.method === 'PUT')).toBe(false);
    expect(go).not.toHaveBeenCalled();
  });

  it('turns the entry off on confirm and opens Available', async () => {
    const { api, go, wrapper } = mountSettings('source-1', { isOperator: true });
    await flushPromises();
    await button(wrapper, 'Turn off')!.trigger('click');
    await flushPromises();
    dialogButton('Turn off')!.click();
    await vi.waitFor(() => expect(go).toHaveBeenCalledWith({ kind: 'available' }));

    expect(api.calls.find((call) => call.method === 'PUT')).toEqual({
      method: 'PUT',
      path: '/api/sources/catalog/ocharo.afisha',
      body: { turnedOn: false },
    });
  });

  it('stays on the page when the switch fails', async () => {
    const { api, go, wrapper } = mountSettings('source-1', { isOperator: true });
    await flushPromises();
    api.failSwitch = { status: 403, title: 'Operators only' };
    await button(wrapper, 'Turn off')!.trigger('click');
    await flushPromises();
    dialogButton('Turn off')!.click();
    await flushPromises();
    expect(api.calls.some((call) => call.method === 'PUT')).toBe(true);
    expect(go).not.toHaveBeenCalled();
  });
});

describe('A source its entry turned off', () => {
  it('opens to a page that says so and links to Available, since the overview leaves it out', async () => {
    const wrapper = mountConsole({
      options: {
        client: createHarvestClient({ fetch: service().fetch }),
        navigation: { href: (place) => `/#${place.kind}`, go: vi.fn() },
      },
      panels: () => h(SourcePanel, { sourceId: 'source-3' }),
    });
    await flushPromises();
    expect(wrapper.text()).toContain('This source is turned off');
    expect(wrapper.text()).not.toContain('Source not found');
    expect(wrapper.get('a').attributes('href')).toBe('/#available');
  });
});
