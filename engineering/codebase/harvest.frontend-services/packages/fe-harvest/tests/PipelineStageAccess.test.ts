import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import HarvestConsoleProvider from '../src/admin/harvestConsole/HarvestConsoleProvider.vue';
import PipelinePanel from '../src/admin/harvestConsole/PipelinePanel.vue';
import type { HarvestConsole } from '../src/admin';
import type { PipelineStageModel } from '../src/contracts/PipelineStagesModel';
import { createHarvestClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

const stages: PipelineStageModel[] = [
  {
    kind: 'process',
    available: true,
    options: { batchSize: 10, processor: 'saved-reading@1' },
    runs: [],
    processorKeys: ['saved-reading@1'],
    processorLabels: { 'saved-reading@1': 'Saved reading' },
  },
];

/** A small service holding one container of the given kind, with the stage routes the console calls. */
function service(containerKind: 'pool' | 'private') {
  const puts: string[] = [];
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
    const method = input instanceof Request ? input.method : (init?.method ?? 'GET');
    const reply = (data: unknown) => Response.json({ data });
    if (url.pathname === '/api/pipelines/pipeline-1/stages') {
      if (method === 'PUT') {
        puts.push(url.pathname);
        return reply(true);
      }
      return reply(stages);
    }
    if (url.pathname === '/api/containers') return reply([{ id: 'container-1', kind: containerKind }]);
    if (url.pathname === '/api/sources')
      return reply([
        { id: 'source-1', handle: 'rentals.example', family: 'site', state: 'active', permission: 'open' },
      ]);
    if (url.pathname === '/api/pipelines')
      return reply([
        {
          id: 'pipeline-1',
          name: 'Rentals',
          state: 'active',
          sourceId: 'source-1',
          scope: { url: 'https://rentals.example/flats', parameters: {} },
          schedules: [],
        },
      ]);
    return reply([]);
  });
  return { fetch, puts };
}

/** A session the way the server sees its caller: an operator or a tenant, over a pool or a private container. */
interface Caller {
  readonly isOperator: boolean;
  readonly container: 'pool' | 'private';
  readonly permissions: readonly string[];
}

async function mountStage({ isOperator, container, permissions }: Caller) {
  const { fetch, puts } = service(container);
  const Console = defineComponent({
    setup: () => () =>
      h(
        HarvestConsoleProvider,
        { client: createHarvestClient({ fetch }), isOperator, permissions },
        {
          default: ({ session }: { session: HarvestConsole }) =>
            session.connected.value ? h(PipelinePanel, { pipelineId: 'pipeline-1', stage: 'process' }) : null,
        },
      ),
  });
  const wrapper = mount(Console, { attachTo: document.body });
  wrappers.push(wrapper);
  await vi.waitFor(() => expect(wrapper.find('article').exists()).toBe(true));
  await flushPromises();
  return { wrapper, puts };
}

const editor = ['Schedule for Process', 'Disable process stage'] as const;
const read = 'pipelines:read';
const manage = 'pipelines:manage';

describe('stage editor access', () => {
  it.each([
    ['an operator with pipelines:manage', { isOperator: true, container: 'pool', permissions: [read, manage] }],
    [
      'a tenant with pipelines:manage in its private container',
      { isOperator: false, container: 'private', permissions: [read, manage] },
    ],
  ] as const)('enables the editor for %s, and saving goes to the stage route', async (_who, caller) => {
    const { wrapper, puts } = await mountStage(caller);

    for (const label of editor) expect(wrapper.find(`[aria-label="${label}"]`).exists(), label).toBe(true);
    expect(wrapper.find('input[aria-label="Batch size for Process"]').exists()).toBe(true);

    await wrapper.get('[aria-label="Disable process stage"]').trigger('click');
    await flushPromises();
    expect(puts).toEqual(['/api/pipelines/pipeline-1/stages']);
  });

  it.each([
    [
      'an operator without pipelines:manage, even holding sources:manage',
      { isOperator: true, container: 'pool', permissions: [read, 'sources:manage'] },
    ],
    [
      'a tenant without pipelines:manage in its private container',
      { isOperator: false, container: 'private', permissions: [read] },
    ],
    [
      'a tenant with pipelines:manage over the shared pool',
      { isOperator: false, container: 'pool', permissions: [read, manage] },
    ],
  ] as const)('disables the editor for %s, as the server refuses it', async (_who, caller) => {
    const { wrapper, puts } = await mountStage(caller);

    for (const label of editor) expect(wrapper.find(`[aria-label="${label}"]`).exists(), label).toBe(false);
    expect(wrapper.find('input[aria-label="Batch size for Process"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('10 posts per run');
    expect(puts).toEqual([]);
  });
});
