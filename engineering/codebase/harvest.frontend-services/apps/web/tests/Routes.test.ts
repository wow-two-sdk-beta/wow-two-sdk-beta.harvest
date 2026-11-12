import { flushPromises, mount } from '@vue/test-utils';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { useGalleryView } from '../src/application/preferences/UseGalleryView';
import { createWebRouter } from '../src/bootstrap/router/Routes';

const wrappers: ReturnType<typeof mount>[] = [];

beforeEach(() => {
  vi.stubEnv('MODE', 'demo');
  vi.stubGlobal('fetch', vi.fn());
});
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
  window.localStorage.clear();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

/** The demo's Afisha.uz, the one harvested source. */
const Afisha = '/sources/00000000-0000-4000-8000-000000000200';

/** Mounts the demo console at `path`, routed as in the browser but over memory history. */
async function renderAt(path: string) {
  const router = createWebRouter(RouterHistoryMode.Memory);
  await router.push(path);
  await router.isReady();
  const wrapper = mount(AppRoot, { global: { plugins: [router] }, attachTo: document.body });
  wrappers.push(wrapper);
  return { router, wrapper };
}

/** Each place: its path, its heading, its current sidebar entry and the region its panel renders. */
const places = [
  ['/', 'Home', 'Home', 'Health'],
  ['/sources', 'Sources', 'Sources', 'Sources'],
  [Afisha, 'Afisha.uz', 'Afisha.uz', 'Sections being harvested'],
  [`${Afisha}/posts`, 'Afisha.uz', 'Afisha.uz', 'Posts'],
  [`${Afisha}/activity`, 'Afisha.uz', 'Afisha.uz', 'Runs'],
  [`${Afisha}/settings`, 'Afisha.uz', 'Afisha.uz', 'Reading'],
  ['/subscribers', 'Subscribers', 'Subscribers', 'Subscribers'],
  ['/access', 'Access', 'Access', 'This session'],
] as const;

describe('console routes', () => {
  it.each(places)('renders %s in the frame with demo data', async (path, heading, entry, region) => {
    const { wrapper } = await renderAt(path);
    await vi.waitFor(() => expect(wrapper.find(`[aria-label="${region}"]`).exists()).toBe(true));
    await vi.waitFor(() => expect(wrapper.get('h1').text()).toContain(heading));
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('nav[aria-label="Console"] [aria-current="page"]').text()).toContain(entry);
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain(entry);
    expect(wrapper.get('[data-app-frame-brand]').text()).toContain('test');
  });

  it.each([
    '/pipelines',
    '/pipelines/00000000-0000-4000-8000-000000000005',
    '/runs',
    '/posts',
    '/subscriptions',
    '/hosts',
  ])('sends the removed page %s to Sources', async (path) => {
    const router = createWebRouter(RouterHistoryMode.Memory);
    await router.push(path);
    expect(router.currentRoute.value.name).toBe('sources');
    expect(router.currentRoute.value.path).toBe('/sources');
  });

  it('names the operator menu in its own words and shows a source under its group', async () => {
    const { wrapper } = await renderAt(Afisha);
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Open source"]').exists()).toBe(true));
    const menu = wrapper.get('nav[aria-label="Console"]').text();
    for (const word of ['Overview', 'Home', 'Collect', 'Sources', 'Deliver', 'Subscribers', 'System', 'Access'])
      expect(menu).toContain(word);
    for (const word of ['Pipelines', 'Runs', 'Hosts', 'Posts', 'Subscriptions']) expect(menu).not.toContain(word);
    expect(wrapper.get('[aria-label="Open source"]').text()).toContain('Afisha');
    expect(wrapper.get('[aria-label="Open source"]').text()).toContain('Afisha.uz');
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Afisha');
    expect(wrapper.find('[aria-label="Workspace"]').exists()).toBe(false);
  });

  it('ends the bar with the account menu and collapses the sidebar to a remembered rail', async () => {
    const { wrapper } = await renderAt('/');
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Health"]').exists()).toBe(true));

    await wrapper.get('button[aria-label="Account menu for Demo operator"]').trigger('click');
    await vi.waitFor(() => expect(document.body.textContent).toContain('Appearance'));
    expect(document.body.textContent).toContain('Console');

    await wrapper.get('button[aria-label="Collapse sidebar"]').trigger('click');
    expect(window.localStorage.getItem('scfd.sidebar-collapsed')).toBe('true');
    await wrapper.get('button[aria-label="Expand sidebar"]').trigger('click');
    expect(window.localStorage.getItem('scfd.sidebar-collapsed')).toBe('false');
  });
});

describe('the Cards | Rows switch', () => {
  it('shows only on Sources and a source’s Posts, with one remembered choice for both', async () => {
    useGalleryView().value = 'cards';
    const { router, wrapper } = await renderAt('/sources');
    const bar = () => wrapper.get('[data-app-frame-bar]');
    await vi.waitFor(() => expect(wrapper.find('article[aria-label="Afisha.uz"]').exists()).toBe(true));
    expect(bar().find('[aria-label="View"]').exists()).toBe(true);

    const rows = bar()
      .findAll('[aria-label="View"] button')
      .find((button) => button.text() === 'Rows')!;
    await rows.trigger('click');
    await flushPromises();
    expect(window.localStorage.getItem('scfd.gallery-view')).toBe('"rows"');
    await vi.waitFor(() => expect(wrapper.find('li[aria-label="Afisha.uz"]').exists()).toBe(true));

    for (const [path, shown] of [
      ['/', false],
      [Afisha, false],
      [`${Afisha}/activity`, false],
      [`${Afisha}/settings`, false],
      ['/subscribers', false],
      ['/access', false],
      [`${Afisha}/posts`, true],
    ] as const) {
      await router.push(path);
      await flushPromises();
      expect(bar().find('[aria-label="View"]').exists(), path).toBe(shown);
    }
    await vi.waitFor(() => expect(wrapper.find('[role="table"][aria-label="Posts"]').exists()).toBe(true));
    expect(
      bar()
        .findAll('[aria-label="View"] button')
        .find((button) => button.text() === 'Rows')!
        .attributes('aria-pressed'),
    ).toBe('true');
  });
});

describe('a post’s page', () => {
  it('opens from the Posts tab at its own address and keeps the source current in the sidebar', async () => {
    const { router, wrapper } = await renderAt(`${Afisha}/posts`);
    await vi.waitFor(() => expect(wrapper.find('a[data-post-link]').exists()).toBe(true));
    const link = wrapper.get('a[data-post-link]');
    expect(link.attributes('href')).toMatch(new RegExp(`^${Afisha}/posts/[0-9a-f-]+$`));
    await link.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('source-post'));
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Post"] h1').exists()).toBe(true));
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('nav[aria-label="Console"] [aria-current="page"]').text()).toContain('Afisha.uz');
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Post');
    await vi.waitFor(() => expect(wrapper.get('[aria-label="Post"]').text()).toContain('Revision 2'));
    expect(wrapper.get('[aria-label="Post"]').text()).toContain('Дворец творчества молодёжи');
  });
});
