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
    expect(posts.value).toHaveLength(10);
    expect(posts.value.filter((row) => row.imageUrl)).toHaveLength(6);
    expect(posts.value.slice(0, 2)).toMatchObject([
      { kind: 'event', startsAt: '2025-11-18', endsAt: '2025-11-18', postedAt: '2025-11-01' },
      { kind: 'event', startsAt: '2026-03-05', endsAt: '2027-06-10', postedAt: '2026-07-21' },
    ]);
    const fashionShow = posts.value.find((row) => row.title === 'Показ мод The Fashion Show Line 2026')!;
    expect(fashionShow.imageUrl).toMatch(/^data:image\/svg\+xml/);
    const show = await api.getPost(fashionShow.postId, container);
    expect(show.ok && show.value.base.media?.filter((item) => item.kind === 'image')).toHaveLength(3);
    const revisions = await api.listPostRevisions(fashionShow.postId);
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

  it('serves the AI views: processed posts with their provenance, and entities with their years, posts and calendar', async () => {
    const api = createDemoHarvestClient();
    const containers = await api.listContainers();
    if (!containers.ok) throw new Error('Demo container missing');
    const container = containers.value[0]!.id;
    const overview = await api.listSourceOverview(container);
    if (!overview.ok) throw new Error('Demo overview missing');
    const afisha = overview.value[0]!.sourceId;
    const processed = await api.querySourceProcessed!(afisha, {});
    if (!processed.ok) throw new Error('Demo processed posts missing');
    expect(processed.value.total).toBeGreaterThan(3);
    expect(processed.value.items.some((post) => post.tags.answer.provider === 'openai')).toBe(true);
    expect(overview.value[0]!.processedCount).toBe(processed.value.total);
    expect(new Set(processed.value.items.map((post) => post.tags.answer.provider))).toEqual(
      new Set(['anthropic', 'openai', 'google']),
    );
    const flash = processed.value.items.find((post) => post.tags.answer.provider === 'google');
    expect(flash?.tags.answer).toMatchObject({ model: 'gemini-2.5-flash' });
    expect(typeof flash?.tags.answer.inputTokens).toBe('number');
    expect(typeof flash?.tags.answer.outputTokens).toBe('number');
    expect(processed.value.items.every((post) => post.state === 'live' && !!post.kind && !!post.pipelineId)).toBe(true);
    expect(new Set(processed.value.items.map((post) => post.language))).toEqual(new Set(['ru', 'en']));
    const show = processed.value.items.find((post) => post.title.startsWith('Показ мод'));
    expect(show).toMatchObject({ kind: 'event', language: 'ru', postedAt: '2026-10-14T09:00:00Z' });
    expect(show?.imageUrl).toBeTruthy();
    const relevant = await api.querySourceProcessed!(afisha, { verdict: 'relevant', extracted: true });
    expect(relevant.ok && relevant.value.items.every((post) => post.extraction?.status === 'done')).toBe(true);
    const paged = await api.querySourceProcessed!(afisha, { limit: 2 });
    expect(paged.ok && paged.value.nextCursor).toBeTruthy();

    const entities = api.entities!;
    const found = await entities.search(container, { q: 'fashion week' });
    if (!found.ok) throw new Error('Demo entities missing');
    const week = found.value[0]!;
    expect(week).toMatchObject({ name: 'Tashkent Fashion Week', cadence: 'annual', containerId: container });
    expect(week.years.some((year) => year.predicted)).toBe(true);
    const timeline = await entities.get(container, week.id);
    expect(timeline.ok && timeline.value.years.length).toBeGreaterThan(2);
    const posts = await entities.posts(container, week.id, { limit: 200 });
    expect(posts.ok && posts.value.data.map((post) => post.role)).toContain('update');
    const today = new Date();
    const next = new Date(today);
    next.setDate(next.getDate() + 365);
    const iso = (date: Date) => date.toISOString().slice(0, 10);
    const calendar = await entities.calendar(container, { from: iso(today), to: iso(next) });
    expect(calendar.ok && calendar.value.some((entry) => entry.occurrence.predicted)).toBe(true);
    expect((await entities.get('another-container', week.id)).ok).toBe(false);
  });
});
