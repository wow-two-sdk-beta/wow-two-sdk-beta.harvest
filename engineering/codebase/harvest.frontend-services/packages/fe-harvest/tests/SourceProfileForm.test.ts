import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import SourceSettingsTab from '../src/admin/harvestConsole/SourceSettingsTab.vue';
import { sourceProfileSchema } from '../src/admin/harvestConsole/SourceProfileSchema';
import type { PipelineModel, SourceOverviewModel } from '../src/contracts';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

const schema = {
  type: 'object',
  properties: {
    url: { type: 'string', format: 'uri', title: 'Publisher profile URL', description: 'A public profile URL.' },
  },
  required: ['url'],
  limits: {
    pageSize: { minimum: 1, maximum: 50, default: 25, title: 'Posts per page' },
    maxPages: { minimum: 1, maximum: 50, default: 4, title: 'Pages per run' },
  },
};
const source: SourceOverviewModel = {
  sourceId: 'source-ig',
  name: 'Instagram',
  family: 'instagram',
  handle: 'www.instagram.com',
  state: 'idle',
  sourceState: 'active',
  newPostsDaily: [],
  sections: [],
  health: { hostKey: 'www.instagram.com', minDelayMs: 5000, consecutiveFailures: 0 },
};
const existing: PipelineModel = {
  id: 'profile-1',
  containerId: 'pool-1',
  sourceId: source.sourceId,
  scopeKey: 'saved-profile-key',
  name: 'Saved publisher',
  state: 'active',
  pinned: true,
  ai: 'off',
  scope: { url: 'https://www.instagram.com/example/', restrictToPath: false, parameters: {} },
  limits: { maxPages: 4, pageSize: 25 },
  steps: {},
  createdAtUtc: '2026-11-17T00:00:00Z',
  schedules: [{ id: 'schedule-1', kind: 'capture', mode: 'interval', every: 'PT12H', timeZone: 'UTC', enabled: false }],
};

function mountProfiles(
  initial: readonly PipelineModel[] = [],
  options: {
    ready?: boolean;
    sourcePaused?: boolean;
    canRun?: boolean;
    workerEnabled?: boolean;
    permissionDenied?: boolean;
  } = {},
) {
  let pipelines = [...initial];
  const shownSource = {
    ...source,
    ...(options.sourcePaused ? { sourceState: 'paused' as const } : {}),
    ...(options.permissionDenied ? { permission: 'denied' as const } : {}),
  };
  const runs: { id: string; pipelineId: string; kind: string; status: string; queuedAtUtc: string }[] = [];
  const writes: { path: string; method: string; body: Record<string, unknown> }[] = [];
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = new URL(String(input), 'https://harvest.example.test').pathname;
    const method = init?.method ?? 'GET';
    if (method !== 'GET') {
      const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>;
      writes.push({ path, method, body });
      if (method === 'POST' && path === '/api/pipelines') {
        const created = { ...existing, ...body, id: 'created-profile' } as PipelineModel;
        pipelines.push(created);
        return Response.json({ data: created });
      }
      if (method === 'PUT' && path === '/api/pipelines/profile-1') {
        pipelines = [{ ...existing, ...body } as PipelineModel];
        return Response.json({ data: pipelines[0] });
      }
      if (method === 'POST' && path === '/api/pipelines/profile-1/pause') {
        pipelines = [{ ...existing, state: body.paused ? 'paused' : 'active' }];
        return Response.json({ data: pipelines[0] });
      }
      if (method === 'POST' && path === '/api/runs') {
        const run = {
          id: 'run-1',
          pipelineId: String(body.pipelineId),
          kind: String(body.kind),
          status: 'queued',
          queuedAtUtc: '2026-11-17T00:00:00Z',
        };
        runs.push(run);
        return Response.json({ data: run });
      }
      throw new Error(`Unexpected write: ${method} ${path}`);
    }
    if (path.endsWith('/capabilities'))
      return Response.json({
        data: {
          sourceId: source.sourceId,
          adapterKey: 'instagram',
          supportsPathScope: true,
          workKinds: ['capture'],
          configuration: {
            displayName: 'Instagram',
            readiness: options.ready === false ? 'configurationRequired' : 'ready',
            scopeSchema: schema,
          },
        },
      });
    const data =
      path === '/api/containers'
        ? [{ id: 'pool-1', kind: 'pool' }]
        : path === '/api/sources'
          ? [{ id: source.sourceId, state: 'active', family: 'instagram', handle: source.handle }]
          : path === '/api/sources/overview'
            ? [shownSource]
            : path === '/api/pipelines'
              ? pipelines
              : path === '/api/runs'
                ? runs
                : [];
    return Response.json({ data });
  });
  const Root = defineComponent({
    setup: () => () =>
      h(
        HarvestConsoleProvider,
        {
          client: createHarvestClient({ fetch }),
          isOperator: true,
          refreshIntervalMs: 0,
          localWorkerEnabled: options.workerEnabled ?? true,
          ...(options.canRun === false ? { permissions: ['pipelines:manage', 'sources:manage'] } : {}),
        },
        { default: () => h(SourceSettingsTab, { source: shownSource }) },
      ),
  });
  const wrapper = mount(Root, { attachTo: document.body });
  wrappers.push(wrapper);
  return { wrapper, writes };
}

describe('stored source profiles', () => {
  it('saves a pinned profile with schema defaults and no schedules or run request', async () => {
    const { wrapper, writes } = mountProfiles();
    await flushPromises();
    const form = wrapper.get('form[aria-label="Add profile"]');
    expect(form.text()).toContain('Publisher profile URL');
    expect(form.findAll('input[type="number"]').map((input) => (input.element as HTMLInputElement).value)).toEqual([
      '25',
      '4',
    ]);
    await form.get('input[type="url"]').setValue('https://www.instagram.com/example/');
    await form.trigger('submit');
    await flushPromises();
    expect(writes).toEqual([
      {
        path: '/api/pipelines',
        method: 'POST',
        body: {
          containerId: 'pool-1',
          sourceId: source.sourceId,
          name: 'example',
          scope: { url: existing.scope.url, restrictToPath: false, parameters: {} },
          limits: { pageSize: 25, maxPages: 4 },
          pinned: true,
          ai: 'off',
          schedules: [],
        },
      },
    ]);
    expect(wrapper.findAll('form[aria-label="Profile limits"]')).toHaveLength(1);
    expect(wrapper.text()).toContain('No harvest is scheduled.');
  });

  it.each([
    ['https://publisher.example.test/people/Some%20Publisher/', 'people/Some Publisher'],
    ['https://publisher.example.test/', 'https://publisher.example.test/'],
  ])('names a custom profile from its URL without a provider-specific rule: %s', async (url, name) => {
    const { wrapper, writes } = mountProfiles();
    await flushPromises();
    const form = wrapper.get('form[aria-label="Add profile"]');
    await form.get('input[type="url"]').setValue(url);
    await form.trigger('submit');
    await flushPromises();
    expect(writes[0]?.body.name).toBe(name);
  });

  it('changes run bounds without replacing scope, name or schedules', async () => {
    const { wrapper, writes } = mountProfiles([existing]);
    await flushPromises();
    const form = wrapper.get('form[aria-label="Profile limits"]');
    const numbers = form.findAll('input[type="number"]');
    await numbers[0]!.setValue('10');
    await numbers[1]!.setValue('2');
    await form.trigger('submit');
    await flushPromises();
    expect(writes).toEqual([
      {
        path: '/api/pipelines/profile-1',
        method: 'PUT',
        body: {
          name: existing.name,
          scope: existing.scope,
          schedules: [{ kind: 'capture', mode: 'interval', every: 'PT12H', timeZone: 'UTC', enabled: false }],
          limits: { pageSize: 10, maxPages: 2 },
        },
      },
    ]);
  });

  it('rejects empty and out-of-range limits without writing', async () => {
    const { wrapper, writes } = mountProfiles([existing]);
    await flushPromises();
    const form = wrapper.get('form[aria-label="Profile limits"]');
    const size = form.get('input[type="number"]');
    expect(size.attributes()).toMatchObject({ min: '1', max: '50', step: '1' });
    for (const value of ['', '0', '51', '1.5']) {
      await size.setValue(value);
      await form.trigger('submit');
      await flushPromises();
    }
    expect(writes).toEqual([]);
  });

  it('enables a saved profile without creating a schedule', async () => {
    const { wrapper, writes } = mountProfiles([{ ...existing, state: 'paused', schedules: [] }]);
    await flushPromises();
    await wrapper.get('[aria-label="Enable Saved publisher"]').trigger('click');
    await flushPromises();
    expect(writes).toEqual([{ path: '/api/pipelines/profile-1/pause', method: 'POST', body: { paused: false } }]);
  });

  it('queues one capture for an unscheduled profile without writing a schedule', async () => {
    const { wrapper, writes } = mountProfiles([{ ...existing, schedules: [] }]);
    await flushPromises();
    const capture = wrapper.get('[aria-label="Capture Saved publisher now"]');
    await capture.trigger('click');
    await flushPromises();
    expect(writes).toEqual([{ path: '/api/runs', method: 'POST', body: { pipelineId: existing.id, kind: 'capture' } }]);
    expect((capture.element as HTMLButtonElement).disabled).toBe(true);
  });

  it.each([
    { ready: false },
    { sourcePaused: true },
    { canRun: false },
    { workerEnabled: false },
    { permissionDenied: true },
    { pipelinePaused: true },
  ])('blocks a capture when its gate is closed: %j', async (options) => {
    const { wrapper, writes } = mountProfiles(
      [{ ...existing, state: 'pipelinePaused' in options ? 'paused' : 'active', schedules: [] }],
      options,
    );
    await flushPromises();
    const capture = wrapper.find('[aria-label="Capture Saved publisher now"]');
    if (capture.exists()) {
      expect((capture.element as HTMLButtonElement).disabled).toBe(true);
      await capture.trigger('click');
    }
    expect(writes).toEqual([]);
  });

  it('offers URL creation only for a required URL schema with valid bounds', () => {
    expect(sourceProfileSchema({ sections: [] })).toBeUndefined();
    expect(sourceProfileSchema({ ...schema, required: [] })).toBeUndefined();
    expect(
      sourceProfileSchema({ ...schema, limits: { pageSize: { minimum: 1, maximum: 50, default: 51 } } }),
    ).toBeUndefined();
  });
});
