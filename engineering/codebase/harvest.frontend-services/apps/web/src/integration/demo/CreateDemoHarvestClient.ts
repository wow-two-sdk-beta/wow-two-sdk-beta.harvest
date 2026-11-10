import { createHarvestClient, type HarvestClient } from '@wow-two-beta/fe-harvest/kit';
import type {
  PipelineModel,
  PostModel,
  PostRevisionModel,
  RunModel,
  ScheduleModel,
  SourceFamily,
  SourceGroupModel,
  SourceHealthModel,
  SourceModel,
  SourceOverviewModel,
  SourceSectionOverviewModel,
} from '@wow-two-beta/fe-harvest/contracts';

/** One synthetic source: its group, how it is read, the sections its reader lists and its earlier daily posts. */
interface DemoSource {
  readonly name: string;
  readonly handle: string;
  readonly family: SourceFamily;
  readonly group: string;
  readonly sections: readonly { readonly label: string; readonly path: string }[];
  /** New posts on each of the 13 days before today, oldest first. */
  readonly history?: readonly number[];
}

/** Twelve sources of seven publishers, as the iteration 1 mocks show them; only Afisha.uz is harvested. */
const DemoSources: readonly DemoSource[] = [
  {
    name: 'Afisha.uz',
    handle: 'www.afisha.uz',
    family: 'site',
    group: 'Afisha',
    sections: [
      { label: 'Fashion', path: '/ru/fashion' },
      { label: 'Exhibitions', path: '/ru/exhibitions' },
      { label: 'Concerts', path: '/ru/concerts' },
      { label: 'Theatre', path: '/ru/theatre' },
      { label: 'Festivals', path: '/ru/festivals' },
      { label: 'For children', path: '/ru/kids' },
    ],
    history: [0, 2, 0, 1, 3, 0, 0, 4, 1, 0, 2, 0, 5],
  },
  { name: 'Afisha Telegram', handle: 'afisha_uz', family: 'telegram', group: 'Afisha', sections: [] },
  { name: 'Afisha Instagram', handle: 'afisha.uz', family: 'instagram', group: 'Afisha', sections: [] },
  {
    name: 'Anons.uz',
    handle: 'anons.uz',
    family: 'site',
    group: 'Anons',
    sections: [
      { label: 'Concerts', path: '/concerts' },
      { label: 'Exhibitions', path: '/exhibitions' },
    ],
  },
  { name: 'Anons Telegram', handle: 'anonsuz', family: 'telegram', group: 'Anons', sections: [] },
  {
    name: 'uzexpocentre.uz',
    handle: 'uzexpocentre.uz',
    family: 'site',
    group: 'Uzexpocentre',
    sections: [{ label: 'Exhibitions', path: '/en/exhibitions' }],
  },
  { name: 'Craft fair site', handle: 'craftfair.uz', family: 'site', group: 'Craft fair', sections: [] },
  { name: 'Craft fair Instagram', handle: 'craftfair.uz', family: 'instagram', group: 'Craft fair', sections: [] },
  { name: 'Teplo · branch 1', handle: 'teplo.uz/chilonzor', family: 'site', group: 'Teplo', sections: [] },
  { name: 'Teplo · branch 2', handle: 'teplo.uz/yunusobod', family: 'site', group: 'Teplo', sections: [] },
  {
    name: 'caex.uz',
    handle: 'caex.uz',
    family: 'site',
    group: 'CAEx',
    sections: [{ label: 'Calendar', path: '/calendar' }],
  },
  {
    name: 'olx.uz',
    handle: 'olx.uz',
    family: 'listing',
    group: 'OLX',
    sections: [{ label: 'Events', path: '/events' }],
  },
];

/** Each publisher's line on its tile. */
const DemoGroups: readonly { readonly name: string; readonly description: string }[] = [
  { name: 'Afisha', description: 'Event listings across Uzbekistan.' },
  { name: 'Anons', description: 'Event announcements.' },
  { name: 'Uzexpocentre', description: 'Exhibition venue calendar.' },
  { name: 'Craft fair', description: 'Craft fairs and markets.' },
  { name: 'Teplo', description: 'A store with several branches.' },
  { name: 'CAEx', description: 'Exhibition venue calendar.' },
  { name: 'OLX', description: 'Classified listings.' },
];

/** A synthetic poster as an inline SVG, so the demo draws a post's images without reaching the network. */
function poster(from: string, to: string, label: string) {
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="640" height="400" viewBox="0 0 640 400">` +
    `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${from}"/>` +
    `<stop offset="1" stop-color="${to}"/></linearGradient></defs><rect width="640" height="400" fill="url(#g)"/>` +
    `<circle cx="510" cy="110" r="74" fill="#fff" fill-opacity=".16"/>` +
    `<circle cx="560" cy="300" r="120" fill="#000" fill-opacity=".08"/>` +
    `<text x="320" y="210" text-anchor="middle" font-family="Helvetica, Arial, sans-serif" font-size="40" font-weight="700" fill="#fff">${label}</text>` +
    `<text x="320" y="248" text-anchor="middle" font-family="Helvetica, Arial, sans-serif" font-size="20" fill="#fff" fill-opacity=".75">synthetic image</text>` +
    `</svg>`;
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

/** A post's images as `media`, each of kind `image`. */
function images(...urls: string[]) {
  return { media: urls.map((url) => ({ kind: 'image', url })) };
}

/** Creates an isolated, synthetic service for UI development. Its transport never reaches the network. */
export function createDemoHarvestClient(): HarvestClient {
  const now = () => new Date().toISOString();
  let sequence = 40;
  const id = () => '00000000-0000-4000-8000-' + String(sequence++).padStart(12, '0');
  const fixed = (n: number) => '00000000-0000-4000-8000-' + String(n).padStart(12, '0');
  const tenantId = fixed(1);
  const containerId = fixed(2);
  const ago = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();
  const tenants = [{ id: tenantId, slug: 'ocharo', name: 'Ocharo', state: 'active', createdAtUtc: ago(60 * 24 * 30) }];
  const containers = [{ id: containerId, kind: 'pool', externalRef: 'Shared pool', createdAtUtc: ago(60 * 24 * 30) }];
  const groups: SourceGroupModel[] = DemoGroups.map((group, index) => ({
    id: fixed(100 + index),
    containerId,
    ...group,
  }));
  const groupId = (name: string) => groups.find((group) => group.name === name)?.id;

  const sources: SourceModel[] = DemoSources.map((demo, index) => ({
    id: fixed(200 + index),
    containerId,
    handle: demo.handle,
    name: demo.name,
    ...(groupId(demo.group) ? { groupId: groupId(demo.group)! } : {}),
    family: demo.family,
    coverage: 'filtered',
    completeness: 'window',
    posture: 'ownFetch',
    permission: 'open',
    robotsPolicy: 'obey',
    aiAllowed: false,
    poolShareable: true,
    consentBasis: 'none',
    authority: 50,
    retentionClass: 'standard',
    capabilityPolicy: {},
    state: 'active',
    stateSinceUtc: ago(60 * 24 * 30),
    createdAtUtc: ago(60 * 24 * 30),
  }));
  const demoOf = (sourceId: string) => DemoSources[sources.findIndex((source) => source.id === sourceId)];
  const sectionUrl = (demo: DemoSource, path: string) => `https://${demo.handle.split('/')[0]}${path}`;
  const health = new Map<string, SourceHealthModel>(
    sources.map((source) => [
      source.id,
      { hostKey: source.handle.split('/')[0]!, minDelayMs: 5000, consecutiveFailures: 0 },
    ]),
  );

  const afisha = sources[0]!;
  const schedule = (kind: ScheduleModel['kind'], every: string): ScheduleModel => ({
    id: id(),
    kind,
    mode: 'interval',
    every,
    enabled: true,
    timeZone: 'Asia/Tashkent',
  });
  const pipelines: PipelineModel[] = [
    { name: 'Afisha · Fashion', label: 'Fashion', path: '/ru/fashion', at: 5 },
    { name: 'Afisha · Exhibitions', label: 'Exhibitions', path: '/ru/exhibitions', at: 6 },
  ].map((row) => ({
    id: fixed(row.at),
    containerId,
    sourceId: afisha.id,
    name: row.name,
    state: 'active',
    pinned: true,
    ai: 'off',
    scopeKey: row.path,
    scope: { url: `https://www.afisha.uz${row.path}`, restrictToPath: true, parameters: {} },
    limits: { maxPages: 5 },
    steps: {},
    schedules: [schedule('capture', 'PT6H'), schedule('check', 'P1D')],
    createdAtUtc: ago(60 * 24 * 14),
  }));
  const [fashion, exhibitions] = pipelines as [PipelineModel, PipelineModel];

  const run = (
    pipeline: PipelineModel,
    minutesAgo: number,
    status: RunModel['status'],
    counts: Partial<RunModel>,
  ): RunModel => ({
    id: id(),
    containerId,
    pipelineId: pipeline.id,
    kind: 'capture',
    status,
    queuedAtUtc: ago(minutesAgo + 2),
    startedAtUtc: ago(minutesAgo + 1),
    finishedAtUtc: ago(minutesAgo),
    captured: 0,
    unchanged: 0,
    discovered: 0,
    failed: 0,
    pages: 1,
    requests: 1,
    bytes: 2048,
    ...counts,
  });
  const runs: RunModel[] = [
    run(fashion, 48, 'partial', { captured: 4, discovered: 23, pages: 5, completionReason: 'page_limit' }),
    run(exhibitions, 48, 'completed', { captured: 4, discovered: 26, unchanged: 22, pages: 3 }),
    run(fashion, 49, 'completed', { kind: 'refresh', captured: 1, unchanged: 3, pages: 1 }),
    run(exhibitions, 60 * 6 + 48, 'failed', {
      failed: 1,
      fetchOutcome: 'timeout',
      message: 'the site did not answer within 30 seconds',
    }),
    run(fashion, 8 * 60 + 40, 'completed', { captured: 4, discovered: 4, pages: 1 }),
  ];

  /** A synthetic post of Afisha.uz, first seen `minutesAgo`. */
  const post = (
    pipeline: PipelineModel,
    title: string,
    kind: string,
    minutesAgo: number,
    extra: Partial<PostModel['base']> = {},
    revision = 1,
  ): PostModel => {
    const postId = id();
    return {
      id: postId,
      containerId,
      state: 'live',
      revision,
      base: { schema: 'wow2.post.v1', kind, title, ...extra },
      links: [
        {
          id: id(),
          sourceId: afisha.id,
          externalKey: postId,
          url: `${pipeline.scope.url}/${postId.slice(-4)}`,
          role: 'primary',
          state: 'live',
          capturedAtUtc: ago(minutesAgo),
        },
      ],
      fieldSources: {},
      firstSeenAtUtc: ago(minutesAgo),
      lastChangedAtUtc: ago(minutesAgo),
    };
  };
  const posts: { post: PostModel; pipelineId: string; startsAt?: string }[] = [
    {
      post: post(
        fashion,
        'Показ мод The Fashion Show Line 2026',
        'event',
        49,
        {
          dates: [{ role: 'start', value: '2026-11-21T12:00:00Z', precision: 'minute' }],
          location: {
            mentions: [
              { text: 'Дворец творчества молодёжи, Ташкент', kind: 'venue' },
              { text: 'просп. Мустакиллик, 2', kind: 'address' },
            ],
          },
          prices: [{ raw: '150 000 сум', currency: 'UZS', role: 'ticket', free: false }],
          postedAt: { value: '2026-10-14T09:00:00Z', precision: 'day' },
          ...images(
            poster('#1f4d3a', '#7fae8f', 'Fashion Show Line'),
            poster('#3b2d5c', '#b07aa1', 'Backstage'),
            poster('#5c3b2d', '#d9a066', 'The runway'),
          ),
        },
        2,
      ),
      pipelineId: fashion.id,
      startsAt: '2026-11-21T12:00:00Z',
    },
    ...(
      [
        ['Autumn collections at a Tashkent showroom', images(poster('#2d3e5c', '#8fa7c9', 'Autumn collections'))],
        ['Five local designers to follow', images(poster('#5c2d3e', '#c98fa7', 'Five designers'))],
        ['A guide to the season’s fabrics', {}],
      ] as const
    ).map(([title, extra]) => ({ post: post(fashion, title, 'article', 48, extra), pipelineId: fashion.id })),
    {
      post: post(
        exhibitions,
        'Ceramics of Rishtan: a retrospective',
        'event',
        48,
        images(poster('#1d4f6b', '#6fb3c9', 'Rishtan ceramics'), poster('#6b4a1d', '#c9a36f', 'The glaze room')),
      ),
      pipelineId: exhibitions.id,
      startsAt: ago(60 * 24 * 3),
    },
    ...[
      'Inside the new photography hall',
      'What to see at the museum this month',
      'Textile art on show in Bukhara',
    ].map((title) => ({ post: post(exhibitions, title, 'article', 48), pipelineId: exhibitions.id })),
  ];
  const revisions = new Map<string, PostRevisionModel[]>(
    posts.map(({ post: row }) => [
      row.id,
      row.revision === 2
        ? [
            { revision: 2, at: ago(49), changed: ['location'] },
            { revision: 1, at: ago(8 * 60 + 40), changed: ['created'] },
          ]
        : [{ revision: 1, at: row.firstSeenAtUtc, changed: ['created'] }],
    ]),
  );

  const subscriptions: Record<string, unknown>[] = [fashion, exhibitions].map((pipeline) => ({
    id: id(),
    tenantId,
    containerId,
    pipelineId: pipeline.id,
    schema: 'wow2.event.v1',
    mode: 'pull',
    state: 'active',
    context: {},
    createdAtUtc: ago(60 * 24 * 10),
  }));
  const events = posts.map(({ post: row }, index) => ({
    cursor: String(index + 1),
    eventId: id(),
    subscriptionId: subscriptions[0]!.id,
    type: 'post.new',
    postId: row.id,
    revision: row.revision,
    occurredAtUtc: row.firstSeenAtUtc,
    data: row.base,
  }));
  const stages = new Map<string, number>();

  /** Minutes in an ISO-8601 interval, for the next harvest. @internal */
  const minutesOf = (every?: string) => {
    const match = every?.match(/^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?)?$/);
    return match ? Number(match[1] ?? 0) * 1440 + Number(match[2] ?? 0) * 60 + Number(match[3] ?? 0) : 0;
  };
  const enabledEvery = (pipeline: PipelineModel, kind: ScheduleModel['kind']) =>
    pipeline.schedules.find((row) => row.kind === kind && row.enabled)?.every;
  const runAt = (row: RunModel) => row.finishedAtUtc ?? row.queuedAtUtc;
  const harvestOf = (row: RunModel) => ({
    runId: row.id,
    pipelineId: row.pipelineId,
    kind: row.kind,
    at: runAt(row),
    status: row.status as 'completed',
    ...(row.completionReason ? { reason: row.completionReason } : {}),
    newPosts: row.captured,
    found: row.discovered,
  });

  /** Builds a source's overview from the demo's own records, as the service does from its tables. */
  const overviewOf = (source: SourceModel): SourceOverviewModel => {
    const demo = demoOf(source.id)!;
    const own = pipelines.filter((pipeline) => pipeline.sourceId === source.id);
    const ownRuns = runs.filter((row) => own.some((pipeline) => pipeline.id === row.pipelineId));
    const sectionRows: SourceSectionOverviewModel[] = own.map((pipeline) => {
      const captureEvery = enabledEvery(pipeline, 'capture');
      const checkEvery = enabledEvery(pipeline, 'check');
      const harvesting = pipeline.state !== 'paused' && !!captureEvery;
      const last = ownRuns.find((row) => row.pipelineId === pipeline.id);
      const lastCapture = ownRuns.find((row) => row.pipelineId === pipeline.id && row.kind === 'capture');
      const maxPages = Number(pipeline.limits['maxPages']);
      return {
        pipelineId: pipeline.id,
        name: pipeline.name ?? source.handle,
        ...(pipeline.scope.url ? { url: pipeline.scope.url } : {}),
        harvesting,
        paused: pipeline.state === 'paused',
        ...(captureEvery ? { captureEvery } : {}),
        ...(checkEvery ? { checkEvery } : {}),
        ...(Number.isFinite(maxPages) && maxPages > 0 ? { maxPages } : {}),
        ...(last ? { lastRun: harvestOf(last) } : {}),
        ...(harvesting
          ? {
              nextDueAt: new Date(
                new Date(lastCapture ? runAt(lastCapture) : now()).getTime() + minutesOf(captureEvery) * 60_000,
              ).toISOString(),
            }
          : {}),
        posts: posts.filter((row) => row.pipelineId === pipeline.id).length,
        subscribers: subscriptions.filter((row) => row.pipelineId === pipeline.id && row.state === 'active').length,
      };
    });
    const lastHarvest = ownRuns.find((row) => row.kind === 'capture');
    const nextHarvestAt = sectionRows
      .map((row) => row.nextDueAt)
      .filter((value): value is string => !!value)
      .sort()[0];
    const host = health.get(source.id)!;
    const paused = !!host.pausedUntil && new Date(host.pausedUntil) > new Date();
    const state = ownRuns.some((row) => row.status === 'queued' || row.status === 'running')
      ? 'harvesting'
      : paused || host.consecutiveFailures >= 3 || lastHarvest?.status === 'failed'
        ? 'needsAttention'
        : sectionRows.some((row) => row.harvesting)
          ? 'healthy'
          : 'idle';
    const today = new Date().toDateString();
    const fresh = posts.filter(
      (row) =>
        own.some((pipeline) => pipeline.id === row.pipelineId) &&
        new Date(row.post.firstSeenAtUtc).toDateString() === today,
    ).length;
    return {
      sourceId: source.id,
      name: source.name ?? source.handle,
      family: source.family,
      handle: source.handle,
      ...(source.groupId ? { groupId: source.groupId } : {}),
      state,
      ...(lastHarvest ? { lastHarvest: harvestOf(lastHarvest) } : {}),
      ...(nextHarvestAt ? { nextHarvestAt } : {}),
      newPostsDaily: [...(demo.history ?? Array.from({ length: 13 }, () => 0)), fresh],
      sections: sectionRows,
      health: host,
    };
  };

  const reply = (data: unknown) => Response.json({ data });
  const missing = () => Response.json({ title: 'Not found in this demo workspace' }, { status: 404 });
  const transport: typeof globalThis.fetch = async (input, init) => {
    const request = input instanceof Request ? input : null;
    const url = new URL(request?.url ?? String(input), 'https://demo.harvest.example.test');
    if (init?.signal?.aborted || request?.signal.aborted) throw new DOMException('Aborted', 'AbortError');
    const method = init?.method ?? request?.method ?? 'GET';
    const body = JSON.parse(typeof init?.body === 'string' ? init.body : '{}') as Record<string, unknown>;
    const path = url.pathname.split('/').filter(Boolean);
    const resource = path[1];
    const itemId = path[2];
    const inContainer = url.searchParams.get('containerId') === containerId;
    if (path[0] !== 'api') return missing();
    if (method === 'GET') {
      if (resource === 'tenants') return reply(tenants);
      if (resource === 'containers') return reply(containers);
      if (resource === 'sources' && itemId === 'overview') return reply(inContainer ? sources.map(overviewOf) : []);
      if (resource === 'sources' && itemId === 'groups') return reply(inContainer ? groups : []);
      if (resource === 'sources' && itemId && path[3] === 'posts')
        return reply(
          posts
            .filter((row) =>
              pipelines.some((pipeline) => pipeline.id === row.pipelineId && pipeline.sourceId === itemId),
            )
            .map(({ post: row, pipelineId, startsAt }) => ({
              postId: row.id,
              title: row.base.title,
              kind: row.base.kind ?? 'post',
              state: row.state,
              revision: row.revision,
              firstSeenAt: row.firstSeenAtUtc,
              lastChangedAt: row.lastChangedAtUtc,
              pipelineId,
              ...(startsAt ? { startsAt } : {}),
              url: row.links[0]?.url,
              ...(row.base.media?.some((item) => item.kind === 'image')
                ? { imageUrl: row.base.media.find((item) => item.kind === 'image')!.url }
                : {}),
            })),
        );
      if (resource === 'sources' && itemId && path[3] === 'capabilities') {
        const demo = demoOf(itemId);
        return demo
          ? reply({
              sourceId: itemId,
              adapterKey: `demo-${demo.group.toLowerCase().replace(/\W+/g, '-')}`,
              supportsPathScope: true,
              workKinds: ['capture', 'discover', 'refresh', 'check'],
              configuration: {
                displayName: demo.group,
                readiness: 'ready',
                message: 'This in-memory demo never executes a real harvest.',
                scopeSchema: {
                  schema: 'harvest.scope.v1',
                  type: 'object',
                  sections: demo.sections.map((section) => ({
                    label: section.label,
                    url: sectionUrl(demo, section.path),
                  })),
                },
              },
            })
          : missing();
      }
      if (resource === 'sources') return reply(inContainer ? sources : []);
      if (resource === 'pipelines') return reply(inContainer ? pipelines : []);
      if (resource === 'posts' && itemId && path[3] === 'revisions') return reply(revisions.get(itemId) ?? []);
      if (resource === 'posts' && itemId) {
        const row = posts.find((item) => item.post.id === itemId);
        return row ? reply(row.post) : missing();
      }
      if (resource === 'posts') return reply(inContainer ? posts.map((row) => row.post) : []);
      if (resource === 'hosts') return reply([]);
      if (resource === 'runs') {
        const visible = runs.filter((row) => row.pipelineId === url.searchParams.get('pipelineId'));
        for (const row of visible) {
          if (!['queued', 'running'].includes(row.status)) continue;
          const stage = (stages.get(row.id) ?? 0) + 1;
          stages.set(row.id, stage);
          Object.assign(
            row,
            stage === 1
              ? { status: 'running', startedAtUtc: now() }
              : { status: 'completed', finishedAtUtc: now(), captured: 0, discovered: 12, unchanged: 12, pages: 2 },
          );
        }
        return reply(itemId ? runs.find((row) => row.id === itemId) : visible);
      }
      if (resource === 'subscriptions' && itemId && path[3] === 'events') {
        if (!subscriptions.some((row) => row.id === itemId)) return missing();
        const cursor = Number(url.searchParams.get('cursor') ?? 0);
        return reply({
          data: events.filter((event) => Number(event.cursor) > cursor),
          next: String(events.length),
          hasMore: false,
        });
      }
      if (resource === 'subscriptions') return reply(subscriptions);
    }
    if (method === 'POST' && resource === 'sources' && itemId === 'groups') {
      const name = String(body.name ?? '').trim();
      const existing = groups.find((group) => group.name.toLowerCase() === name.toLowerCase());
      if (existing) return reply(existing);
      const row: SourceGroupModel = {
        id: id(),
        containerId,
        name,
        ...(body.description ? { description: String(body.description) } : {}),
      };
      groups.push(row);
      return reply(row);
    }
    if (method === 'PUT' && resource === 'sources' && itemId && path[3] === 'reading') {
      const host = health.get(itemId);
      if (!host) return missing();
      const next = { ...host, minDelayMs: Number(body.minDelayMs) };
      health.set(itemId, next);
      return reply(next);
    }
    if (method === 'PUT' && resource === 'sources' && itemId) {
      const index = sources.findIndex((row) => row.id === itemId);
      if (index < 0) return missing();
      const current = { ...sources[index]! } as { -readonly [K in keyof SourceModel]: SourceModel[K] };
      if (typeof body.name === 'string' && body.name.trim()) current.name = body.name.trim();
      if (body.clearGroup) delete current.groupId;
      else if (typeof body.groupId === 'string') current.groupId = body.groupId;
      sources[index] = current;
      return reply(current);
    }
    if (method === 'POST' && resource === 'tenants') {
      const row = { id: id(), slug: String(body.slug), name: String(body.name), state: 'active', createdAtUtc: now() };
      tenants.push(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'containers') {
      const row = { id: id(), kind: String(body.kind), externalRef: 'New demo workspace', createdAtUtc: now() };
      containers.push(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'pipelines' && !itemId) {
      const row: PipelineModel = {
        id: id(),
        containerId,
        sourceId: String(body.sourceId),
        ...(typeof body.name === 'string' ? { name: body.name } : {}),
        state: 'active',
        pinned: true,
        ai: 'off',
        scopeKey: String((body.scope as { url?: string } | undefined)?.url ?? '*'),
        scope: { parameters: {}, ...(body.scope as Partial<PipelineModel['scope']>) },
        limits: { maxPages: 5 },
        steps: {},
        schedules: ((body.schedules as Omit<ScheduleModel, 'id'>[] | undefined) ?? []).map((row) => ({
          ...row,
          id: id(),
        })),
        createdAtUtc: now(),
      };
      pipelines.push(row);
      return reply(row);
    }
    if (resource === 'pipelines' && itemId) {
      const index = pipelines.findIndex((row) => row.id === itemId);
      if (index < 0) return missing();
      if (method === 'PUT') {
        const limits = body.limits as { maxPages?: number } | undefined;
        pipelines[index] = {
          ...pipelines[index]!,
          ...(typeof body.name === 'string' ? { name: body.name } : {}),
          scope: body.scope as PipelineModel['scope'],
          schedules: ((body.schedules as Omit<ScheduleModel, 'id'>[] | undefined) ?? []).map((row) => ({
            ...row,
            id: id(),
          })),
          ...(limits?.maxPages ? { limits: { ...pipelines[index]!.limits, maxPages: limits.maxPages } } : {}),
        };
        return reply(pipelines[index]);
      }
      if (method === 'POST' && path[3] === 'pause') {
        pipelines[index] = { ...pipelines[index]!, state: body.paused ? 'paused' : 'active' };
        return reply(pipelines[index]);
      }
    }
    if (method === 'POST' && resource === 'runs' && !itemId) {
      const pipeline = pipelines.find((row) => row.id === body.pipelineId);
      if (!pipeline) return missing();
      if (pipeline.state !== 'active') return Response.json({ title: 'Turn this section on first' }, { status: 409 });
      const row: RunModel = {
        id: id(),
        containerId: pipeline.containerId,
        pipelineId: pipeline.id,
        kind: (body.kind ?? 'capture') as RunModel['kind'],
        status: 'queued',
        queuedAtUtc: now(),
        captured: 0,
        unchanged: 0,
        discovered: 0,
        failed: 0,
        pages: 0,
        requests: 0,
        bytes: 0,
      };
      runs.unshift(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'runs' && itemId && path[3] === 'cancel') {
      const row = runs.find((item) => item.id === itemId);
      if (!row) return missing();
      Object.assign(row, { status: 'cancelled', finishedAtUtc: now() });
      return reply(row);
    }
    if (method === 'POST' && resource === 'subscriptions') {
      const row = { ...body, id: id(), mode: 'pull', state: 'active', context: {}, createdAtUtc: now() };
      subscriptions.push(row);
      return reply(row);
    }
    return missing();
  };
  return createHarvestClient({ fetch: transport });
}
