import { DOMWrapper, flushPromises, mount } from '@vue/test-utils';
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
function service(containerKind: 'pool' | 'private', rows: readonly PipelineStageModel[] = stages) {
  const puts: string[] = [];
  const bodies: { stage: Record<string, unknown> }[] = [];
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
    const method = input instanceof Request ? input.method : (init?.method ?? 'GET');
    const reply = (data: unknown) => Response.json({ data });
    if (url.pathname === '/api/pipelines/pipeline-1/stages') {
      if (method === 'PUT') {
        puts.push(url.pathname);
        bodies.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
        return reply(true);
      }
      return reply(rows);
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
  return { fetch, puts, bodies };
}

/** A session the way the server sees its caller: an operator or a tenant, over a pool or a private container. */
interface Caller {
  readonly isOperator: boolean;
  readonly container: 'pool' | 'private';
  readonly permissions: readonly string[];
}

async function mountStage(
  { isOperator, container, permissions }: Caller,
  rows: readonly PipelineStageModel[] = stages,
  kind: 'discover' | 'harvest' | 'process' = 'process',
) {
  const { fetch, puts, bodies } = service(container, rows);
  const Console = defineComponent({
    setup: () => () =>
      h(
        HarvestConsoleProvider,
        { client: createHarvestClient({ fetch }), isOperator, permissions },
        {
          default: ({ session }: { session: HarvestConsole }) =>
            session.connected.value ? h(PipelinePanel, { pipelineId: 'pipeline-1', stage: kind }) : null,
        },
      ),
  });
  const wrapper = mount(Console, { attachTo: document.body });
  wrappers.push(wrapper);
  await vi.waitFor(() => expect(wrapper.find('article').exists()).toBe(true));
  await flushPromises();
  return { wrapper, puts, bodies };
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

describe('stage requests settings', () => {
  const schedule = {
    id: 'schedule-1',
    mode: 'interval',
    every: 'PT15S',
    timeZone: 'Asia/Tashkent',
    enabled: true,
  } as const;
  const harvest: PipelineStageModel = {
    kind: 'harvest',
    available: true,
    options: { batchSize: 50, gapMs: 250, parallel: 4 },
    schedule: { ...schedule, kind: 'harvest', stage: { batchSize: 50, gapMs: 250, parallel: 4 } },
    runs: [],
  };
  const discover: PipelineStageModel = {
    kind: 'discover',
    available: true,
    options: { batchSize: 5, walk: 'full', gapMs: 100 },
    schedule: { ...schedule, kind: 'discover', every: 'PT5M', stage: { batchSize: 5, walk: 'full', gapMs: 100 } },
    runs: [],
  };
  const operator = { isOperator: true, container: 'pool', permissions: [read, manage] } as const;
  const tenant = { isOperator: false, container: 'private', permissions: [read, manage] } as const;

  async function openSettings(wrapper: ReturnType<typeof mount>, kind: 'Discover' | 'Harvest') {
    await wrapper.get(`[aria-label="Schedule for ${kind}"]`).trigger('click');
    const body = new DOMWrapper(document.body);
    return vi.waitFor(() => body.get('[role="dialog"]'));
  }

  it('shows the walk, the gap and the parallel fetches on the stage card', async () => {
    const { wrapper } = await mountStage(operator, [harvest, discover], 'harvest');
    expect(wrapper.text()).toContain('250 ms apart');
    expect(wrapper.text()).toContain('4 at once');
    expect(wrapper.text()).toContain('Every 15 s');
    const { wrapper: walking } = await mountStage(operator, [harvest, discover], 'discover');
    expect(walking.text()).toContain('Whole catalog each run');
    expect(walking.text()).toContain('100 ms apart');
  });

  it('lets an operator change the gap and the parallel fetches, in seconds for a harvest interval', async () => {
    const { wrapper, bodies } = await mountStage(operator, [harvest, discover], 'harvest');
    const dialog = await openSettings(wrapper, 'Harvest');
    const every = dialog.get('input[type="number"][min="15"]');
    expect((every.element as HTMLInputElement).value).toBe('15');
    const [gap, parallel] = dialog.findAll('input[type="number"][step="1"]');
    expect(gap!.attributes('disabled')).toBeUndefined();
    expect(parallel!.attributes('disabled')).toBeUndefined();

    await gap!.setValue('0');
    await parallel!.setValue('8');
    await dialog.get('form').trigger('submit');
    await flushPromises();

    expect(bodies).toHaveLength(1);
    expect(bodies[0]).toMatchObject({ every: 'PT15S', stage: { batchSize: 50, gapMs: 0, parallel: 8 } });
  });

  it.each([
    ['harvest', 'Harvest', { batchSize: 50, gapMs: 250, parallel: 4 }],
    ['discover', 'Discover', { batchSize: 5, walk: 'full', gapMs: 100 }],
  ] as const)(
    'keeps the %s walk, gap and parallel fetches read-only for a tenant, and its save keeps them',
    async (kind, label, expected) => {
      const { wrapper, puts, bodies } = await mountStage(tenant, [harvest, discover], kind);
      const dialog = await openSettings(wrapper, label);
      const fields = dialog.findAll('input[type="number"][step="1"]');
      expect(fields.length).toBeGreaterThan(0);
      for (const field of fields) expect(field.attributes('disabled'), kind).toBeDefined();
      expect(dialog.text()).toContain('Only an operator sets the gap and the parallel fetches');

      await dialog.get('form').trigger('submit');
      await flushPromises();

      expect(puts).toEqual(['/api/pipelines/pipeline-1/stages']);
      expect(bodies[0]!.stage).toEqual(expected);
    },
  );

  it('keeps the walk, the gap and the parallel fetches when an operator saves the dialog untouched', async () => {
    const { wrapper, bodies } = await mountStage(operator, [harvest, discover], 'discover');
    const dialog = await openSettings(wrapper, 'Discover');
    await dialog.get('form').trigger('submit');
    await flushPromises();
    expect(bodies[0]!.stage).toEqual({ batchSize: 5, walk: 'full', gapMs: 100 });
  });

  it('keeps the gap and the parallel fetches when a tenant changes the batch from the card', async () => {
    const { wrapper, bodies } = await mountStage(tenant, [harvest, discover], 'harvest');
    const batch = wrapper.get('input[aria-label="Batch size for Harvest"]');
    await batch.setValue('120');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(bodies[0]!.stage).toMatchObject({ batchSize: 120, gapMs: 250, parallel: 4 });
  });
});
