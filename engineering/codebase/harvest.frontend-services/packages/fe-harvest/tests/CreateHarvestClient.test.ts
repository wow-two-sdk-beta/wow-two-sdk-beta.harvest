import { describe, expect, it, vi } from 'vitest';
import { createHarvestClient } from '../src/kit';

describe('Harvest console client', () => {
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
