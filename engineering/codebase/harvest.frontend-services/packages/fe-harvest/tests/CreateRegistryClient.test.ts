import { describe, expect, it, vi } from 'vitest';
import { createRegistryClient } from '../src/kit';

const serviceUrl = 'https://harvest.example.test';
const container = {
  id: 'c-1',
  kind: 'pool',
  embeddingsEnabled: false,
  state: 'active',
  createdAtUtc: '2026-10-27T00:00:00Z',
};

function clientAnswering(response: () => Response, apiKey?: string) {
  const fetch = vi.fn<typeof globalThis.fetch>(async () => response());
  return { fetch, client: createRegistryClient({ baseUrl: `${serviceUrl}/`, fetch, ...(apiKey ? { apiKey } : {}) }) };
}

function requestOf(fetch: ReturnType<typeof vi.fn<typeof globalThis.fetch>>) {
  const [input, init] = fetch.mock.calls[0] ?? [];
  return input instanceof Request ? input : new Request(String(input), init);
}

describe('registry client', () => {
  it('reads containers from the envelope and sends the key', async () => {
    const { fetch, client } = clientAnswering(() => Response.json({ data: [container] }), 'hv_key');

    const result = await client.listContainers();

    expect(result.ok && result.value).toEqual([container]);
    const request = requestOf(fetch);
    expect(request.url).toBe(`${serviceUrl}/api/containers`);
    expect(request.headers.get('X-Api-Key')).toBe('hv_key');
  });

  it('filters sources and pipelines by container', async () => {
    const { fetch, client } = clientAnswering(() => Response.json({ data: [] }));

    await client.listPipelines('c-1');

    expect(requestOf(fetch).url).toBe(`${serviceUrl}/api/pipelines?containerId=c-1`);
  });

  it('rejects a list whose items carry no id', async () => {
    const { client } = clientAnswering(() => Response.json({ data: [{ kind: 'pool' }] }));

    const result = await client.listSources('c-1');

    expect(result.ok).toBe(false);
  });
});
