import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => wrappers.splice(0).forEach((wrapper) => wrapper.unmount()));

async function fixture(legacy = false) {
  const fail = new Set<string>();
  let state = 'active';
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : null;
    const path = new URL(request?.url ?? String(input), 'https://example.test').pathname;
    const method = init?.method ?? request?.method ?? 'GET';
    if (fail.has(path)) return Response.json({ title: 'Temporarily unavailable' }, { status: 503 });
    if (method === 'PUT' && path === '/api/sources/source') {
      state = JSON.parse(String(init?.body)).paused ? 'paused' : 'active';
      return Response.json({ data: { id: 'source', state } });
    }
    if (method === 'POST' && path === '/api/runs') return Response.json({ data: { id: 'new-run', status: 'queued' } });
    const source = { id: 'source', state, permission: 'open', family: 'site', handle: 'example.test' };
    const data =
      path === '/api/containers'
        ? [{ id: 'container', kind: 'pool' }]
        : path === '/api/sources'
          ? [source]
          : path === '/api/pipelines'
            ? [
                { id: 'pipeline', sourceId: 'source', state: 'active', scope: {}, schedules: [] },
                { id: 'paused', sourceId: 'source', state: 'paused', scope: {}, schedules: [] },
              ]
            : path === '/api/sources/overview'
              ? [
                  {
                    sourceId: 'source',
                    name: 'Example',
                    family: 'site',
                    handle: 'example.test',
                    ...(legacy ? {} : { sourceState: state }),
                    state: 'healthy',
                    newPostsDaily: [],
                    health: {},
                    sections: [
                      { pipelineId: 'pipeline', harvesting: false, paused: false, harvestKind: 'discover' },
                      { pipelineId: 'paused', harvesting: false, paused: true },
                    ],
                  },
                ]
              : path.endsWith('/capabilities')
                ? {
                    sourceId: 'source',
                    workKinds: ['discover'],
                    configuration: { displayName: 'Example', readiness: 'ready', scopeSchema: {} },
                  }
                : path === '/api/runs'
                  ? [{ id: 'run', pipelineId: 'pipeline', status: 'completed', queuedAtUtc: '2026-11-17T00:00:00Z' }]
                  : [];
    return Response.json({ data });
  });
  let session!: HarvestConsole;
  const wrapper = mount(HarvestConsoleProvider, {
    props: { client: createHarvestClient({ fetch }), refreshIntervalMs: 60_000 },
    slots: {
      default: (props: { session: HarvestConsole }) => {
        session = props.session;
        return [];
      },
    },
  });
  wrappers.push(wrapper);
  await flushPromises();
  await session.refresh();
  return { session, fail, fetch };
}

describe('source session recovery and master control', () => {
  it('retains successful list and activity snapshots during failures and recovers on retry', async () => {
    const { session, fail } = await fixture();
    fail.add('/api/sources');
    fail.add('/api/sources/overview');
    fail.add('/api/runs');
    await session.refresh();
    expect(session.sourceListState.value).toBe('stale');
    expect(session.overviewState.value).toBe('stale');
    expect(session.sources.value).toHaveLength(1);
    expect(session.overviews.value).toHaveLength(1);
    expect(session.runStateOf('pipeline')).toBe('stale');
    expect(session.runsOf('pipeline')[0]?.id).toBe('run');
    fail.clear();
    await session.retryRuns(['pipeline']);
    expect(session.runStateOf('pipeline')).toBe('ready');
    await session.refresh();
    expect(session.sourceListState.value).toBe('ready');
    expect(session.overviewState.value).toBe('ready');
  });

  it('can retry capability failure without discarding its last usable metadata', async () => {
    const { session, fail } = await fixture();
    fail.add('/api/sources/source/capabilities');
    await session.retryCapabilities('source');
    expect(session.capabilityErrors.value['source']).toBeTruthy();
    expect(session.capabilities.value['source']?.configuration.readiness).toBe('ready');
    fail.clear();
    await session.retryCapabilities('source');
    expect(session.capabilityErrors.value['source']).toBe('');
  });

  it('changes the source gate in one write without altering individually paused sections', async () => {
    const { session, fetch } = await fixture();
    expect(await session.setSourcePaused('source', true)).toBe(true);
    expect(session.sourceOf('source')?.state).toBe('paused');
    expect(await session.harvestNow('source')).toBe(false);
    expect(await session.setSourcePaused('source', false)).toBe(true);
    expect(session.pipelineOf('paused')?.state).toBe('paused');
    const writes = fetch.mock.calls.filter(([, init]) => init?.method === 'PUT' || init?.method === 'POST');
    expect(writes.map(([url]) => new URL(String(url), 'https://example.test').pathname)).toEqual([
      '/api/sources/source',
      '/api/sources/source',
    ]);
  });

  it('queues a manual staged section using its configured kind without requiring an automatic schedule', async () => {
    const { session, fetch } = await fixture();
    expect(await session.harvestNow('source')).toBe(true);
    const writes = fetch.mock.calls.filter(([, init]) => init?.method === 'POST');
    expect(writes).toHaveLength(1);
    expect(JSON.parse(String(writes[0]![1]?.body))).toMatchObject({ pipelineId: 'pipeline', kind: 'discover' });
  });

  it('keeps a completed source pause visible when only the overview refresh fails', async () => {
    const { session, fail } = await fixture();
    fail.add('/api/sources/overview');
    expect(await session.setSourcePaused('source', true)).toBe(true);
    expect(session.overviewState.value).toBe('stale');
    expect(session.overviewOf('source')?.sourceState).toBe('paused');
    expect(session.overviewOf('source')?.permission).toBe('open');
    expect(session.overviewOf('source')?.sections).toHaveLength(2);
  });

  it('does not submit a master pause to an older server, including after a failed overview refresh', async () => {
    const { session, fail, fetch } = await fixture(true);
    expect(await session.setSourcePaused('source', true)).toBe(false);
    fail.add('/api/sources/overview');
    await session.refresh();
    expect(await session.setSourcePaused('source', true)).toBe(false);
    expect(session.failure.value).toContain('unavailable on this server version');
    expect(fetch.mock.calls.filter(([, init]) => init?.method === 'PUT')).toHaveLength(0);
  });
});
