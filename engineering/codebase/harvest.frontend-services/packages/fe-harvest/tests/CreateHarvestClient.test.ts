import { describe, expect, it, vi } from 'vitest';
import { createHarvestClient } from '../src/kit';

describe('Harvest console client', () => {
  it('addresses the API from the origin root, so a nested console page reaches it', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: [] }));
    const api = createHarvestClient({ fetch });
    await api.listTenants();
    await api.listRuns('pipeline');
    await api.listPosts('container');
    await api.listSubscriptions();
    await api.getSourceCapabilities('source');
    for (const [url] of fetch.mock.calls)
      expect(new URL(String(url), 'https://console.example/pipelines/one').pathname).toMatch(/^\/api\//);
  });
  it('reads and writes the Sources area on the contract routes, from the origin root', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
      const path = new URL(String(input), 'https://console.example/sources/one/posts').pathname;
      return Response.json({
        data:
          path === '/api/sources/overview'
            ? [{ sourceId: 's', name: 'Afisha.uz', state: 'idle', newPostsDaily: [], sections: [], health: {} }]
            : path === '/api/sources/s/posts'
              ? [{ postId: 'p', kind: 'event' }]
              : path.endsWith('/revisions') || path === '/api/sources/groups'
                ? []
                : { id: 'x' },
      });
    });
    const api = createHarvestClient({ fetch });
    expect((await api.listSourceOverview('c')).ok).toBe(true);
    expect((await api.listSourceGroups('c')).ok).toBe(true);
    expect((await api.listSourcePosts('s')).ok).toBe(true);
    expect((await api.listPostRevisions('p')).ok).toBe(true);
    expect((await api.getPost('p', 'c')).ok).toBe(true);
    await api.createSourceGroup({ containerId: 'c', name: 'Afisha' });
    await api.updateSource('s', { groupId: null, clearGroup: true });
    await api.setSourceReading('s', { minDelayMs: 5000 });
    const calls = fetch.mock.calls.map(([url, init]) => {
      const address = new URL(String(url), 'https://console.example/sources/one/posts');
      return `${init?.method ?? 'GET'} ${address.pathname}${address.search}`;
    });
    expect(calls).toEqual([
      'GET /api/sources/overview?containerId=c',
      'GET /api/sources/groups?containerId=c',
      'GET /api/sources/s/posts?limit=200',
      'GET /api/posts/p/revisions',
      'GET /api/posts/p?containerId=c',
      'POST /api/sources/groups',
      'PUT /api/sources/s',
      'PUT /api/sources/s/reading',
    ]);
    expect(JSON.parse(String(fetch.mock.calls[6]?.[1]?.body))).toEqual({ groupId: null, clearGroup: true });
  });

  describe('source catalog', () => {
    /** A catalog entry as the service answers it. */
    const entry = {
      key: 'ocharo.afisha',
      catalog: 'ocharo',
      name: 'Afisha',
      description: null,
      kind: 'events',
      hosts: [{ family: 'site', handle: 'www.afisha.uz' }],
      channels: 4,
      state: 'new',
      changedAtUtc: null,
      changedBy: null,
      sources: [],
    };

    it('reads the catalog from its route at the origin root', async () => {
      const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: [entry] }));
      const result = await createHarvestClient({ fetch }).listSourceCatalog();
      expect(result.ok && result.value).toEqual([entry]);
      const [url, init] = fetch.mock.calls[0]!;
      const address = new URL(String(url), 'https://console.example/available');
      expect(`${init?.method ?? 'GET'} ${address.pathname}${address.search}`).toBe('GET /api/sources/catalog');
    });

    it('rejects a catalog entry without its state or its sources', async () => {
      for (const bad of [
        { ...entry, state: 'maybe' },
        { ...entry, sources: undefined },
        { ...entry, key: 7 },
      ]) {
        const api = createHarvestClient({ fetch: async () => Response.json({ data: [bad] }) });
        expect((await api.listSourceCatalog()).ok).toBe(false);
      }
      const notAList = createHarvestClient({ fetch: async () => Response.json({ data: entry }) });
      expect((await notAList.listSourceCatalog()).ok).toBe(false);
    });

    it('switches one entry with its key in the path and the choice in the body', async () => {
      const turnedOn = { ...entry, state: 'on', sources: [{ sourceId: 's', containerId: 'c', name: 'Afisha.uz' }] };
      const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: turnedOn }));
      const api = createHarvestClient({ fetch });
      const result = await api.setCatalogSourceTurnedOn('marketplace.olx-uz', true);
      expect(result.ok && result.value.state).toBe('on');
      await api.setCatalogSourceTurnedOn('marketplace.olx-uz', false);
      const [url, init] = fetch.mock.calls[0]!;
      const address = new URL(String(url), 'https://console.example/available');
      expect(`${init?.method} ${address.pathname}`).toBe('PUT /api/sources/catalog/marketplace.olx-uz');
      expect(JSON.parse(String(init?.body))).toEqual({ turnedOn: true });
      expect(JSON.parse(String(fetch.mock.calls[1]?.[1]?.body))).toEqual({ turnedOn: false });
    });

    it('escapes a key that is not path-safe', async () => {
      const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: entry }));
      await createHarvestClient({ fetch }).setCatalogSourceTurnedOn('odd key/1', true);
      expect(String(fetch.mock.calls[0]?.[0])).toContain('/api/sources/catalog/odd%20key%2F1');
    });

    it('rejects a switch answer that is not an entry', async () => {
      const api = createHarvestClient({ fetch: async () => Response.json({ data: { key: 'ocharo.afisha' } }) });
      expect((await api.setCatalogSourceTurnedOn('ocharo.afisha', true)).ok).toBe(false);
    });

    it.each([
      [404, 'Unknown source'],
      [403, 'Operators only'],
      [409, 'Setting up Afisha failed: the container is missing'],
    ])('maps a %s answer to a failure that carries its status and the service’s reason', async (status, title) => {
      const api = createHarvestClient({ fetch: async () => Response.json({ title }, { status }) });
      const result = await api.setCatalogSourceTurnedOn('ocharo.afisha', true);
      expect(result.ok).toBe(false);
      if (result.ok) return;
      expect(result.failure.status).toBe(status);
      expect(result.failure.problem?.['title']).toBe(title);
    });

    it('maps a refused catalog read to a failure', async () => {
      const api = createHarvestClient({ fetch: async () => Response.json({ title: 'Forbidden' }, { status: 403 }) });
      const result = await api.listSourceCatalog();
      expect(!result.ok && result.failure.status).toBe(403);
    });
  });

  it('rejects an overview row without its health', async () => {
    const api = createHarvestClient({
      fetch: async () =>
        Response.json({ data: [{ sourceId: 's', name: 'n', state: 'idle', newPostsDaily: [], sections: [] }] }),
    });
    expect((await api.listSourceOverview('c')).ok).toBe(false);
  });

  it('requests processing evidence for one source and preserves a container-wide read', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: [] }));
    const api = createHarvestClient({ fetch });
    expect((await api.listPostProcessing?.('p', 'c', new AbortController().signal, 's'))?.ok).toBe(true);
    expect((await api.listPostProcessing?.('p', 'c'))?.ok).toBe(true);
    const requests = fetch.mock.calls.map(([url]) => new URL(String(url), 'https://console.example/source/post'));
    expect(requests.map((url) => url.pathname)).toEqual(['/api/posts/p/processing', '/api/posts/p/processing']);
    expect(requests.map((url) => Object.fromEntries(url.searchParams))).toEqual([
      { containerId: 'c', sourceId: 's', limit: '50' },
      { containerId: 'c', limit: '50' },
    ]);
  });

  it('reads safe runtime capabilities and rejects malformed discovery', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () =>
      Response.json({
        data: {
          sourceId: 'source',
          workKinds: ['capture'],
          configuration: { displayName: 'External adapter', readiness: 'ready', scopeSchema: {} },
        },
      }),
    );
    const api = createHarvestClient({ fetch });
    expect((await api.getSourceCapabilities('source')).ok).toBe(true);
    expect(String(fetch.mock.calls[0]?.[0])).toContain('api/sources/source/capabilities');
    const malformed = createHarvestClient({ fetch: async () => Response.json({ data: { sourceId: 'source' } }) });
    expect((await malformed.getSourceCapabilities('source')).ok).toBe(false);
  });
  it('updates one pipeline with a complete saved target and schedule body', async () => {
    const fetch = vi.fn(async () => Response.json({ data: { id: 'saved-search' } }));
    const update = {
      name: 'Filtered search',
      scope: { url: 'https://olx.uz/search?q=sewing&city=1', parameters: {} },
      schedules: [],
    };
    await createHarvestClient({ fetch }).updatePipeline('saved-search', update);
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain('api/pipelines/saved-search');
    expect(init.method).toBe('PUT');
    expect(JSON.parse(String(init.body))).toEqual(update);
  });
  it('carries the key in a header and advances only the server cursor', async () => {
    const fetch = vi.fn(async () => Response.json({ data: { data: [], next: 'opaque-safe-cursor', hasMore: false } }));
    const api = createHarvestClient({ apiKey: 'hv_fixture', baseUrl: 'https://harvest.example.test', fetch });
    const result = await api.pull('subscription', 'previous:cursor');
    expect(result.ok && result.value.next).toBe('opaque-safe-cursor');
    const [url, init] = fetch.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toContain('cursor=previous%3Acursor');
    expect(url).not.toContain('hv_fixture');
    expect(new Headers(init.headers).get('X-Api-Key')).toBe('hv_fixture');
  });

  it('rejects malformed cursor responses', async () => {
    const api = createHarvestClient({ fetch: vi.fn(async () => Response.json({ data: { data: [], next: 42 } })) });
    const result = await api.pull('subscription');
    expect(result.ok).toBe(false);
  });
});
