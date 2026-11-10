import { mount } from '@vue/test-utils';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { createWebRouter } from '../src/bootstrap/router/Routes';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

/** Each place: its path, the heading it shows, its sidebar entry and the region its panel renders. */
const places = [
  ['/', 'Home', 'Home', 'Health'],
  ['/sources', 'Sources', 'Sources', 'Sources'],
  ['/pipelines', 'Pipelines', 'Pipelines', 'Pipelines'],
  ['/pipelines/00000000-0000-4000-8000-000000000005', 'Events · demo', 'Pipelines', 'Pipeline overview'],
  ['/runs', 'Runs', 'Runs', 'Runs'],
  ['/posts', 'Posts', 'Posts', 'Posts'],
  ['/subscriptions', 'Subscriptions', 'Subscriptions', 'Subscriptions'],
  ['/hosts', 'Hosts', 'Hosts', 'Host health'],
  ['/access', 'Access', 'Access', 'Tenants'],
] as const;

describe('console routes', () => {
  it.each(places)('renders %s in the frame with demo data', async (path, heading, entry, region) => {
    vi.stubEnv('MODE', 'demo');
    vi.stubGlobal('fetch', vi.fn());
    const router = createWebRouter(RouterHistoryMode.Memory);
    await router.push(path);
    await router.isReady();
    const wrapper = mount(AppRoot, { global: { plugins: [router] } });
    wrappers.push(wrapper);

    await vi.waitFor(() => expect(wrapper.find(`[aria-label="${region}"]`).exists()).toBe(true));
    await vi.waitFor(() => expect(wrapper.get('h1').text()).toBe(heading));
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('nav[aria-label="Console"] [aria-current="page"]').text()).toBe(entry);
    expect(wrapper.get('header[role="banner"]').text()).toContain(entry);
    expect(wrapper.find('[aria-label="App version"]').text()).toBe('test');
  });

  it('ends the bar with the account menu and collapses the sidebar to a remembered rail', async () => {
    vi.stubEnv('MODE', 'demo');
    vi.stubGlobal('fetch', vi.fn());
    const router = createWebRouter(RouterHistoryMode.Memory);
    await router.push('/');
    await router.isReady();
    const wrapper = mount(AppRoot, { global: { plugins: [router] }, attachTo: document.body });
    wrappers.push(wrapper);
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Health"]').exists()).toBe(true));

    await wrapper.get('button[aria-label="Account menu for Demo operator"]').trigger('click');
    await vi.waitFor(() => expect(document.body.textContent).toContain('Appearance'));
    expect(document.body.textContent).toContain('Dark');
    expect(document.body.textContent).toContain('Console');

    await wrapper.get('button[aria-label="Collapse sidebar"]').trigger('click');
    expect(window.localStorage.getItem('scfd.sidebar-collapsed')).toBe('true');
    expect(wrapper.get('nav[aria-label="Console"]').attributes('data-collapsed')).toBeDefined();
    await wrapper.get('button[aria-label="Expand sidebar"]').trigger('click');
    expect(window.localStorage.getItem('scfd.sidebar-collapsed')).toBe('false');
  });
});
