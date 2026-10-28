import { createHarvestClient, type HarvestClient } from '@wow-two-beta/fe-harvest/kit';
import type { PipelineModel, PostModel, RunModel, SourceModel } from '@wow-two-beta/fe-harvest/contracts';

/** Creates an isolated, synthetic service for UI development. Its transport never reaches the network. */
export function createDemoHarvestClient(): HarvestClient {
  const now = () => new Date().toISOString();
  let sequence = 20;
  const id = () => '00000000-0000-4000-8000-' + String(sequence++).padStart(12, '0');
  const tenantId = '00000000-0000-4000-8000-000000000001';
  const containerId = '00000000-0000-4000-8000-000000000002';
  const tenants = [{ id: tenantId, slug: 'demo', name: 'Demo workspace', state: 'active', createdAtUtc: now() }];
  const containers = [
    { id: containerId, kind: 'private', tenantId, externalRef: 'Demo · synthetic pipelines', createdAtUtc: now() },
  ];
  const sources: SourceModel[] = ['events.example.test', 'listings.example.test'].map((handle, index) => ({
    id: '00000000-0000-4000-8000-00000000000' + (index + 3),
    containerId,
    handle,
    family: 'site',
    coverage: 'filtered',
    completeness: 'window',
    posture: 'ownFetch',
    permission: 'open',
    robotsPolicy: 'obey',
    aiAllowed: false,
    poolShareable: false,
    consentBasis: 'none',
    authority: 50,
    retentionClass: 'standard',
    capabilityPolicy: {},
    state: 'active',
    stateSinceUtc: now(),
    createdAtUtc: now(),
  }));
  const pipelines: PipelineModel[] = sources.map((source, index) => ({
    id: '00000000-0000-4000-8000-00000000000' + (index + 5),
    containerId,
    sourceId: source.id,
    name: index === 0 ? 'Events · demo' : 'Listings · demo',
    state: 'active',
    pinned: true,
    ai: 'off',
    scopeKey: source.handle,
    scope: { url: 'https://' + source.handle + '/upcoming?city=demo', parameters: {} },
    limits: { maxPages: 3, maxItems: 2, maxRequests: 8 },
    steps: {},
    schedules: [],
    createdAtUtc: now(),
  }));
  const posts: PostModel[] = sources.map((source, index) => ({
    id: id(),
    containerId,
    state: 'live',
    revision: 1,
    base: {
      schema: 'wow2.post.v1',
      kind: index === 0 ? 'event' : 'listing',
      title: index === 0 ? 'Design market · synthetic example' : 'Studio equipment · synthetic example',
      text: 'Synthetic content for developing the shared console. No real source or provider is contacted.',
      postedAt: { value: now(), precision: 'instant' },
      author: { name: 'Demo publisher' },
      prices: [
        {
          raw: index === 0 ? 'Free' : '250 USD',
          currency: index === 0 ? undefined : 'USD',
          free: index === 0,
          role: index === 0 ? 'ticket' : 'listed',
        },
      ],
      sourceTags: ['demo', 'synthetic'],
    },
    links: [
      {
        id: id(),
        sourceId: source.id,
        externalKey: 'demo-' + index,
        url: 'https://' + source.handle + '/example',
        role: 'primary',
        state: 'live',
        capturedAtUtc: now(),
        lastCheckedAtUtc: now(),
        lastSeenAtUtc: now(),
        lastCheckOutcome: 'present',
        lastCheckHttpStatus: 200,
        lastCheckRunKind: 'capture',
        checkCount: 1,
        sourceFields: { schema: 'demo.source.v1', fields: { title: 'Original synthetic title', category: 'design' } },
      },
    ],
    fieldSources: {},
    firstSeenAtUtc: now(),
    lastChangedAtUtc: now(),
  }));
  const runs: RunModel[] = [];
  const subscriptions: Record<string, unknown>[] = [
    {
      id: id(),
      tenantId,
      containerId,
      pipelineId: pipelines[0]!.id,
      schema: 'wow2.post.v1',
      mode: 'pull',
      state: 'active',
      context: {},
      createdAtUtc: now(),
    },
  ];
  const events = posts.map((post, index) => ({
    cursor: String(index + 1),
    eventId: id(),
    subscriptionId: subscriptions[0]!.id,
    type: 'post.new',
    postId: post.id,
    revision: 1,
    occurredAtUtc: now(),
    data: post.base,
  }));
  const stages = new Map<string, number>();
  const schema = {
    schema: 'harvest.scope.v1',
    type: 'object',
    properties: {
      url: {
        type: 'string',
        format: 'uri',
        description: 'A synthetic saved URL. The demo makes no external requests.',
      },
      parameters: { type: 'object', additionalProperties: { type: 'string' } },
    },
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
    if (path[0] !== 'api') return missing();
    if (method === 'GET') {
      if (resource === 'tenants') return reply(tenants);
      if (resource === 'containers') return reply(containers);
      if (resource === 'sources' && itemId && path[3] === 'capabilities') {
        const source = sources.find((row) => row.id === itemId);
        return source
          ? reply({
              sourceId: itemId,
              adapterKey: 'demo-source',
              workKinds: ['capture', 'discover', 'refresh', 'check'],
              configuration: {
                displayName: 'Synthetic source adapter',
                readiness: 'ready',
                message: 'This in-memory demo never executes a real harvest.',
                scopeSchema: schema,
              },
            })
          : missing();
      }
      if (resource === 'sources')
        return reply(sources.filter((row) => row.containerId === url.searchParams.get('containerId')));
      if (resource === 'pipelines')
        return reply(pipelines.filter((row) => row.containerId === url.searchParams.get('containerId')));
      if (resource === 'posts')
        return reply(posts.filter((row) => row.containerId === url.searchParams.get('containerId')));
      if (resource === 'hosts') return reply([]);
      if (resource === 'runs') {
        const visible = runs.filter((run) => run.pipelineId === url.searchParams.get('pipelineId'));
        for (const run of visible) {
          if (!['queued', 'running'].includes(run.status)) continue;
          const stage = (stages.get(run.id) ?? 0) + 1;
          stages.set(run.id, stage);
          Object.assign(
            run,
            stage === 1
              ? { status: 'running', startedAtUtc: now() }
              : {
                  status: 'completed',
                  finishedAtUtc: now(),
                  captured: 0,
                  unchanged: 1,
                  pages: 1,
                  completionReason: 'exhausted',
                },
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
    if (method === 'POST' && resource === 'tenants') {
      const row = { id: id(), slug: String(body.slug), name: String(body.name), state: 'active', createdAtUtc: now() };
      tenants.push(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'containers') {
      const row = {
        id: id(),
        kind: String(body.kind),
        tenantId: String(body.tenantId ?? tenantId),
        externalRef: 'New demo container',
        createdAtUtc: now(),
      };
      containers.push(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'sources') {
      const row = { ...sources[0]!, ...body, id: id(), createdAtUtc: now() } as SourceModel;
      sources.push(row);
      return reply(row);
    }
    if (method === 'POST' && resource === 'pipelines' && !itemId) {
      const row = { ...pipelines[0]!, ...body, id: id(), createdAtUtc: now() } as PipelineModel;
      pipelines.push(row);
      return reply(row);
    }
    if (resource === 'pipelines' && itemId) {
      const index = pipelines.findIndex((row) => row.id === itemId);
      if (index < 0) return missing();
      if (method === 'PUT') {
        pipelines[index] = {
          ...pipelines[index]!,
          name: body.name as string | undefined,
          scope: body.scope as PipelineModel['scope'],
          schedules: body.schedules as PipelineModel['schedules'],
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
      if (pipeline.state !== 'active')
        return Response.json({ title: 'Resume this demo pipeline first' }, { status: 409 });
      const run: RunModel = {
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
      runs.unshift(run);
      return reply(run);
    }
    if (method === 'POST' && resource === 'runs' && itemId && path[3] === 'cancel') {
      const run = runs.find((row) => row.id === itemId);
      if (!run) return missing();
      Object.assign(run, { status: 'cancelled', finishedAtUtc: now() });
      return reply(run);
    }
    if (method === 'POST' && resource === 'subscriptions') {
      const row = { ...body, id: id() };
      subscriptions.push(row);
      return reply(row);
    }
    return missing();
  };
  return createHarvestClient({ fetch: transport });
}
