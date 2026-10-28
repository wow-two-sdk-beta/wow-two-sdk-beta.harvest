import { describe, expect, it, vi } from 'vitest';
import { createDemoHarvestClient } from '../src/integration/demo/CreateDemoHarvestClient';

describe('standalone demo client', () => {
  it('edits pipelines, runs and pulls delivery without calling a backend or real source', async () => {
    const network = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('No network in demo'));
    const api = createDemoHarvestClient();
    const containers = await api.listContainers();
    expect(containers.ok).toBe(true);
    if (!containers.ok) return;
    const container = containers.value[0]!;
    const pipelines = await api.listPipelines(container.id);
    if (!pipelines.ok) throw new Error('Demo pipelines missing');
    const original = pipelines.value[0]!;
    const metadata = await api.getSourceCapabilities(original.sourceId);
    expect(metadata.ok && metadata.value.configuration.readiness).toBe('ready');
    const saved = await api.updatePipeline(original.id, {
      name: 'Edited synthetic pipeline',
      scope: { url: 'https://events.example.test/filter?city=demo', parameters: { category: 'design' } },
      schedules: [],
    });
    expect(saved.ok && saved.value.name).toBe('Edited synthetic pipeline');
    const started = await api.startRun(original.id);
    expect(started.ok && started.value.status).toBe('queued');
    const running = await api.listRuns(original.id);
    expect(running.ok && running.value[0]?.status).toBe('running');
    const completed = await api.listRuns(original.id);
    expect(completed.ok && completed.value[0]?.status).toBe('completed');
    const subscriptions = await api.listSubscriptions();
    if (!subscriptions.ok) throw new Error('Demo subscription missing');
    const first = await api.pull(subscriptions.value[0]!.id);
    if (!first.ok) throw new Error('Demo delivery missing');
    expect(first.value.data.length).toBeGreaterThan(0);
    const next = await api.pull(subscriptions.value[0]!.id, first.value.next);
    expect(next.ok && next.value.data).toEqual([]);
    expect(network).not.toHaveBeenCalled();
    network.mockRestore();
  });

  it('keeps changes local to one demo instance and honours cancellation', async () => {
    const first = createDemoHarvestClient();
    const second = createDemoHarvestClient();
    const rows = await first.listContainers();
    if (!rows.ok) throw new Error('Missing fixture');
    const pipelines = await first.listPipelines(rows.value[0]!.id);
    if (!pipelines.ok) throw new Error('Missing fixture');
    const pipeline = pipelines.value[0]!;
    await first.setPipelinePaused(pipeline.id, true);
    const untouched = await second.listPipelines(rows.value[0]!.id);
    expect(untouched.ok && untouched.value[0]?.state).toBe('active');
    const controller = new AbortController();
    controller.abort();
    expect((await first.listContainers(controller.signal)).ok).toBe(false);
  });
});
