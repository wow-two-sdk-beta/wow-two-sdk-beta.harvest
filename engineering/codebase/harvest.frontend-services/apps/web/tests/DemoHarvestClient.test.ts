import { describe, expect, it, vi } from 'vitest';
import { createDemoHarvestClient } from '../src/integration/demo/CreateDemoHarvestClient';

describe('standalone demo client', () => {
  it('edits sections, runs harvests and pulls delivery without calling a backend or real source', async () => {
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
      name: 'Edited synthetic section',
      scope: original.scope,
      schedules: [],
      limits: { maxPages: 9 },
    });
    expect(saved.ok && saved.value.name).toBe('Edited synthetic section');
    expect(saved.ok && saved.value.limits['maxPages']).toBe(9);
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

  it('serves the Sources area: twelve sources in seven groups, Afisha.uz harvested, its posts and revisions', async () => {
    const api = createDemoHarvestClient();
    const containers = await api.listContainers();
    if (!containers.ok) throw new Error('Demo container missing');
    const container = containers.value[0]!.id;
    const overview = await api.listSourceOverview(container);
    if (!overview.ok) throw new Error('Demo overview missing');
    expect(overview.value).toHaveLength(12);
    const afisha = overview.value[0]!;
    expect(afisha).toMatchObject({
      name: 'Afisha.uz',
      state: 'healthy',
      lastHarvest: { status: 'partial', newPosts: 4 },
    });
    expect(afisha.newPostsDaily).toHaveLength(14);
    expect(afisha.sections.filter((section) => section.harvesting)).toHaveLength(2);
    expect(overview.value.filter((source) => !source.lastHarvest)).toHaveLength(11);
    const groups = await api.listSourceGroups(container);
    expect(groups.ok && groups.value.map((group) => group.name)).toContain('Teplo');
    const posts = await api.listSourcePosts(afisha.sourceId);
    if (!posts.ok) throw new Error('Demo posts missing');
    expect(posts.value).toHaveLength(8);
    expect(posts.value.filter((row) => row.imageUrl)).toHaveLength(4);
    expect(posts.value[0]!.imageUrl).toMatch(/^data:image\/svg\+xml/);
    const show = await api.getPost(posts.value[0]!.postId, container);
    expect(show.ok && show.value.base.media?.filter((item) => item.kind === 'image')).toHaveLength(3);
    const revisions = await api.listPostRevisions(posts.value[0]!.postId);
    expect(revisions.ok && revisions.value.map((row) => row.changed)).toEqual([['location'], ['created']]);
    const again = await api.createSourceGroup({ containerId: container, name: 'afisha' });
    expect(again.ok && again.value.name).toBe('Afisha');
    const reading = await api.setSourceReading(afisha.sourceId, { minDelayMs: 10000 });
    expect(reading.ok && reading.value.minDelayMs).toBe(10000);
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
