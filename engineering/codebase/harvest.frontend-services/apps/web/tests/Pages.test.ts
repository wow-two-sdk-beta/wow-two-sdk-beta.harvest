import { flushPromises, mount } from '@vue/test-utils';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { createWebRouter } from '../src/bootstrap/router/Routes';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.unstubAllGlobals();
});

const status = { data: { service: 'harvest', status: 'ok', version: '0.1.4+abc1234' } };

/** Answers the console's two same-origin calls: the open status probe, and the registry, which wants a key. */
function answer(input: RequestInfo | URL) {
  const url = new URL(input instanceof Request ? input.url : String(input), 'https://harvest.example.test');
  return url.pathname === '/api/system/status'
    ? Response.json(status)
    : Response.json({ title: 'Unauthorized' }, { status: 401 });
}

/** Mounts the app at `path`, routed as in the browser but over memory history. */
async function renderAt(path: string) {
  const router = createWebRouter(RouterHistoryMode.Memory);
  await router.push(path);
  await router.isReady();
  const wrapper = mount(AppRoot, { global: { plugins: [router] } });
  wrappers.push(wrapper);
  await flushPromises();
  return wrapper;
}

describe('console route', () => {
  it('shows the running versions and composes the reusable console connection panel', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => answer(input)),
    );
    const wrapper = await renderAt('/');

    expect(wrapper.get('[aria-label="App version"]').text()).toBe('app test');
    expect(wrapper.get('[aria-label="API version"]').text()).toBe('api 0.1.4+abc1234');
    expect(wrapper.text()).toContain('Connect to Harvest');
    expect(wrapper.find('input[type="password"]').exists()).toBe(true);
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
  });
});
