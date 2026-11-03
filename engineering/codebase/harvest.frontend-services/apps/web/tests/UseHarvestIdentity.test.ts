import { defineComponent, h } from 'vue';
import { AuthProvider, type BffUser } from '@wow-two-beta/fe/auth';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createHarvestIdentity,
  HarvestIdentityKey,
  useHarvestIdentity,
} from '../src/application/identity/UseHarvestIdentity';

const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.unstubAllGlobals();
});

function start() {
  let identity!: ReturnType<typeof useHarvestIdentity>;
  const runtime = createHarvestIdentity();
  const child = defineComponent({
    setup() {
      identity = useHarvestIdentity();
      return () => null;
    },
  });
  wrappers.push(
    mount(AuthProvider<BffUser, string | undefined>, {
      props: { strategy: runtime.strategy, bridge: runtime.bridge, onResolveError: runtime.resolveError },
      global: { provide: { [HarvestIdentityKey as symbol]: runtime } },
      slots: { default: () => h(child) },
    }),
  );
  return () => identity;
}

function runtime({ apiKeyMode = false, hasAccess = true } = {}) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = new URL(input instanceof Request ? input.url : String(input), window.location.origin).pathname;
    if (path === '/api/console/access')
      return Response.json({
        data: {
          identityEnabled: !apiKeyMode,
          authenticated: true,
          hasAccess,
          scopes: ['pipelines:read', 'pipelines:run'],
        },
      });
    if (path === '/api/identity/session')
      return Response.json({
        data: {
          isAuthenticated: true,
          person: { sub: 'person-1', name: 'Reviewer' },
          csrfToken: 'session-bound-token',
        },
      });
    if (path === '/api/runs' && (init?.method ?? (input instanceof Request ? input.method : 'GET')) === 'POST')
      return Response.json({ data: { id: 'run-1' } });
    return Response.json({ data: [] });
  });
}

describe('Harvest Identity transport', () => {
  it('uses the BFF cookie and its CSRF token without sending an API key', async () => {
    const fetch = runtime();
    vi.stubGlobal('fetch', fetch);
    const identity = start();
    await flushPromises();
    expect(identity().signedIn.value).toBe(true);
    expect(identity().accountName.value).toBe('Reviewer');
    const result = await identity().client.value!.startRun('pipeline-1', 'capture');
    expect(result, JSON.stringify(result)).toMatchObject({ ok: true });
    const call = fetch.mock.calls.find(
      ([input]) =>
        new URL(input instanceof Request ? input.url : String(input), window.location.origin).pathname === '/api/runs',
    )!;
    expect(new Headers(call[1]?.headers).get('X-Csrf-Token')).toBe('session-bound-token');
    expect(new Headers(call[1]?.headers).has('X-Api-Key')).toBe(false);
    expect(call[1]?.credentials).toBe('same-origin');
  });

  it('drops the connection when an API reports the session expired', async () => {
    const fetch = runtime();
    vi.stubGlobal('fetch', fetch);
    const identity = start();
    await flushPromises();
    fetch.mockImplementationOnce(async () => new Response(null, { status: 401 }));
    await identity().client.value!.listContainers();
    expect(identity().client.value).toBeUndefined();
    expect(identity().signedIn.value).toBe(false);
    expect(identity().permissions.value).toEqual([]);
    expect(identity().message.value).toContain('session has ended');
  });

  it('keeps an unassigned account out of the console without falling back to a key', async () => {
    vi.stubGlobal('fetch', runtime({ hasAccess: false }));
    const identity = start();
    await flushPromises();
    expect(identity().identityEnabled.value).toBe(true);
    expect(identity().signedIn.value).toBe(true);
    expect(identity().client.value).toBeUndefined();
    expect(identity().message.value).toContain('no access');
  });

  it('preserves explicitly configured API-key mode without calling Identity session routes', async () => {
    const fetch = runtime({ apiKeyMode: true });
    vi.stubGlobal('fetch', fetch);
    const identity = start();
    await flushPromises();
    expect(identity().ready.value).toBe(true);
    expect(identity().identityEnabled.value).toBe(false);
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('cancels previous account requests and ignores their late 401 after an account switch', async () => {
    const fetch = runtime();
    vi.stubGlobal('fetch', fetch);
    const identity = start();
    await flushPromises();
    const oldClient = identity().client.value!;
    let finish!: (response: Response) => void;
    let oldSignal!: AbortSignal;
    fetch.mockImplementationOnce(async (_input, init) => {
      oldSignal = init!.signal!;
      return new Promise<Response>((resolve) => {
        finish = resolve;
      });
    });
    const oldRequest = oldClient.listContainers();
    await flushPromises();
    const normal = runtime();
    fetch.mockImplementation(async (input, init) => {
      if (String(input) === '/api/identity/session')
        return Response.json({
          data: {
            isAuthenticated: true,
            person: { sub: 'person-2', name: 'Another reviewer' },
            csrfToken: 'another-session-token',
          },
        });
      return normal(input, init);
    });
    await identity().load();
    expect(oldSignal.aborted).toBe(true);
    expect(identity().client.value).not.toBe(oldClient);
    finish(new Response(null, { status: 401 }));
    await oldRequest;
    expect(identity().signedIn.value).toBe(true);
    expect(identity().accountName.value).toBe('Another reviewer');
    expect(identity().client.value).toBeDefined();
  });
});
