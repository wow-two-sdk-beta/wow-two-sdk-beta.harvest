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
const FashionPipeline = '/pipelines/00000000-0000-4000-8000-000000000005';
/** The demo's Tashkent Fashion Week entity. */
const FashionWeek = '00000000-0000-4000-8000-000000000900';

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
  ['/available', 'Available', 'Available', 'Available sources'],
  [Afisha, 'Afisha.uz', 'Afisha.uz', 'Sections being harvested'],
  [`${Afisha}/posts`, 'Afisha.uz', 'Afisha.uz', 'Posts'],
  [`${Afisha}/processed`, 'Afisha.uz', 'Afisha.uz', 'Filter processed posts'],
  [`${Afisha}/activity`, 'Afisha.uz', 'Afisha.uz', 'Runs'],
  [`${Afisha}/settings`, 'Afisha.uz', 'Afisha.uz', 'Reading'],
  ['/pipelines', 'Pipelines', 'Pipelines', 'Pipelines'],
  [FashionPipeline, 'Fashion', 'Fashion', 'Section pipelines'],
  ['/entities', 'Entities', 'Entities', 'Entities'],
  [`/entities/${FashionWeek}`, 'Entity', 'Entities', 'Entity'],
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

  it.each(['/runs', '/posts', '/subscriptions', '/hosts'])('sends the removed page %s to Sources', async (path) => {
    const router = createWebRouter(RouterHistoryMode.Memory);
    await router.push(path);
    expect(router.currentRoute.value.name).toBe('sources');
    expect(router.currentRoute.value.path).toBe('/sources');
  });

  it('names the operator menu in its own words and shows a source under its group', async () => {
    const { wrapper } = await renderAt(Afisha);
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Open source"]').exists()).toBe(true));
    const menu = wrapper.get('nav[aria-label="Console"]').text();
    for (const word of [
      'Overview',
      'Home',
      'Collect',
      'Sources',
      'Available',
      'Pipelines',
      'Entities',
      'Deliver',
      'Subscribers',
      'System',
      'Access',
    ])
      expect(menu).toContain(word);
    for (const word of ['Runs', 'Hosts', 'Posts', 'Subscriptions']) expect(menu).not.toContain(word);
    expect(wrapper.get('[aria-label="Open source"]').text()).toContain('Afisha');
    expect(wrapper.get('[aria-label="Open source"]').text()).toContain('Afisha.uz');
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Afisha');
    expect(wrapper.find('[aria-label="Workspace"]').exists()).toBe(false);
  });

  it('shows what the AI made of a post, and links an entity post to its post page', async () => {
    const { router, wrapper } = await renderAt(`${Afisha}/processed`);
    await vi.waitFor(() => expect(wrapper.findAll('article[aria-label]').length).toBeGreaterThan(0));
    const show = wrapper.get('article[aria-label="Показ мод The Fashion Show Line 2026"]');
    expect(show.text()).toContain('Relevant');
    expect(show.get('header [data-ai-brand]').attributes('data-ai-brand')).toBe('claude');
    expect(show.get('header [role="img"]').attributes('aria-label')).toBe('Claude Haiku 5.5');
    expect(show.find('footer').exists()).toBe(false);
    expect(show.get('[aria-label="Event extraction"]').text()).toContain('Youth Creativity Palace');
    const href = show.get('a[data-processed-link]').attributes('href')!;
    expect(href).toMatch(new RegExp(`^${Afisha}/processed/`));

    await router.push(`/entities/${FashionWeek}`);
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Entity"]').exists()).toBe(true));
    const postHref = href.replace('/processed/', '/posts/');
    expect(wrapper.get(`a[href="${postHref}"]`).text()).toContain('Показ мод The Fashion Show Line 2026');
  });

  it('opens a processed card on its own page, in the frame, with the trail ending in the post and its links', async () => {
    const { router, wrapper } = await renderAt(`${Afisha}/processed`);
    await vi.waitFor(() => expect(wrapper.findAll('article[aria-label]').length).toBeGreaterThan(0));
    const title = 'Показ мод The Fashion Show Line 2026';
    const link = wrapper.get(`article[aria-label="${title}"] a[data-processed-link]`);
    const postId = link.attributes('href')!.split('/').at(-1)!;
    await link.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`${Afisha}/processed/${postId}`));
    expect(router.currentRoute.value.name).toBe('source-processed-post');
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Processed post"] h1').exists()).toBe(true));
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('h1').text()).toBe(title);
    expect(wrapper.get('nav[aria-label="Console"] [aria-current="page"]').text()).toContain('Afisha.uz');

    const bar = wrapper.get('[data-app-frame-bar]');
    await vi.waitFor(() => expect(bar.text()).toContain(title));
    const trail = bar.text();
    expect(trail.indexOf('Collect')).toBeLessThan(trail.indexOf('Sources'));
    expect(trail.indexOf('Afisha.uz')).toBeLessThan(trail.indexOf('Processed'));
    expect(trail.indexOf('Processed')).toBeLessThan(trail.indexOf(title));
    expect(bar.find(`a[href="${Afisha}/processed"]`).exists()).toBe(true);

    for (const section of ['Classification', 'Tags', 'Event extraction', 'AI passes', 'Links'])
      expect(wrapper.find(`section[aria-label="${section}"]`).exists()).toBe(true);
    expect(wrapper.get('[aria-label="Event extraction"] blockquote').text()).toContain('21 ноября');
    expect(wrapper.get('[aria-label="Links"] a[href$="/posts/' + postId + '"]').text()).toBe('Open post');
    expect(wrapper.get('[aria-label="Links"] a[target="_blank"]').text()).toContain('Open original');

    await wrapper.get('[aria-label="Links"] a[href$="/posts/' + postId + '"]').trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('source-post'));
  });

  it('answers a processed post that is not in the source with the page’s error state', async () => {
    const { wrapper } = await renderAt(`${Afisha}/processed/00000000-0000-4000-8000-00000000ffff`);
    await vi.waitFor(() => expect(wrapper.text()).toContain('Could not read this processed post'));
    expect(wrapper.findAll('h1')).toHaveLength(0);
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Processed');
  });

  it('opens a pipeline from the directory and links to a specific run audit', async () => {
    const { router, wrapper } = await renderAt('/pipelines');
    const pipeline = `a[href="${FashionPipeline}"]`;
    await vi.waitFor(() => expect(wrapper.find(pipeline).exists()).toBe(true));
    await wrapper.get(pipeline).trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(FashionPipeline));
    await vi.waitFor(() =>
      expect(wrapper.find('[aria-label="Section pipelines"] a[aria-label="Open Process pipeline"]').exists()).toBe(
        true,
      ),
    );
    const views = () => wrapper.get('nav[aria-label="Pipeline views"]');
    expect(views().get('[aria-current="page"]').text()).toBe('Overview');
    await views().get(`a[href="${FashionPipeline}/discoveries"]`).trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`${FashionPipeline}/discoveries`));
    await views().get(`a[href="${FashionPipeline}/logs"]`).trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`${FashionPipeline}/logs`));
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Run history"] a').exists()).toBe(true));
    const run = wrapper.get('[aria-label="Run history"] a');
    const href = run.attributes('href');
    expect(href).toMatch(new RegExp(`^${FashionPipeline}/runs/[0-9a-f-]+$`));
    await run.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(href));
    await vi.waitFor(() => expect(wrapper.get('[aria-label="Run monitor"]').text()).toContain('Selected run'));
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Run audit');
    const sidebar = () => wrapper.get('nav[aria-label="Console"]');
    expect(sidebar().get('[aria-current="page"]').text()).toBe('Fashion');
    expect(wrapper.get(`[aria-label="Run monitor"] a[href="${FashionPipeline}/logs"]`).text()).toBe('Follow live');
    await sidebar().get('a[href="/subscribers"]').trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/subscribers'));
    expect(sidebar().get('[aria-current="page"]').attributes('href')).toBe('/subscribers');
    await sidebar().get('a[aria-label="Open Fashion pipelines"]').trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(FashionPipeline));
    const node = '[aria-label="Section pipelines"] a[aria-label="Open Harvest pipeline"]';
    await vi.waitFor(() => expect(wrapper.find(node).exists()).toBe(true));
    await wrapper.get(node).trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`${FashionPipeline}/stages/harvest`));
    await vi.waitFor(() =>
      expect(wrapper.find('[aria-label="Pipeline steps"] [data-operation="save-capture"]').exists()).toBe(true),
    );
    await wrapper.get('button[data-operation="save-capture"]').trigger('click');
    expect(wrapper.get('[aria-label="Step details"]').text()).toContain('Save capture');
    expect(wrapper.find('[aria-label="Section pipelines"]').exists()).toBe(false);
    await sidebar().get('a[href="/subscribers"]').trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/subscribers'));
    await sidebar().get('a[aria-label="Open Harvest pipeline for Fashion"]').trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe(`${FashionPipeline}/stages/harvest`));
  });

  it('opens stage details from the source card and edits its schedule in a dialog', async () => {
    const { router, wrapper } = await renderAt(`${Afisha}/pipelines`);
    const link = 'a[aria-label="Open Discover pipeline"]';
    await vi.waitFor(() => expect(wrapper.find(link).exists()).toBe(true));
    expect(wrapper.find('details').exists()).toBe(false);
    expect(wrapper.get(link).attributes('href')).toBe(`${FashionPipeline}/stages/discover`);
    await wrapper.get(link).trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('pipeline-stage'));
    await vi.waitFor(() => expect(wrapper.get('h1').text()).toBe('Discover pipeline'));
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Discover');
    const settings = 'button[aria-label="Schedule for Discover"]';
    await vi.waitFor(() => expect(wrapper.find(settings).exists()).toBe(true));
    expect(wrapper.get(settings).element.closest('article')?.querySelector('input[type="number"]')).toBeNull();
    await wrapper.get(settings).trigger('click');
    await vi.waitFor(() =>
      expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('Discover schedule'),
    );
    const dialog = document.body.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Fixed interval');
    expect(dialog.textContent).toContain('Every (minutes)');
    expect(dialog.textContent).toContain('Enable scheduled runs');
    expect(dialog.querySelector('input[type="date"]')).toBeNull();
    expect(dialog.querySelector('[aria-label="Time zone"]')).toBeNull();
    expect(dialog.textContent).not.toContain('Catalog URL');
    expect(dialog.textContent).not.toContain('Cron expression');
    const choice = (label: string) => dialog.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!;
    const choose = async (label: string, value: string) => {
      choice(label).click();
      const option = () =>
        [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
          (entry) => entry.textContent?.trim() === value,
        );
      await vi.waitFor(() => expect(option()).toBeDefined());
      option()!.click();
      await vi.waitFor(() => expect(choice(label).textContent).toContain(value));
    };
    await choose('Schedule', 'Calendar');
    await choose('Repeat', 'Monthly');
    await choose('Day of each month', 'Last day');
    expect(choice('Time zone').textContent).toContain('Asia/Tashkent');
    await choose('Time zone', 'UTC');
    const date = dialog.querySelector('input[type="date"]') as HTMLInputElement;
    date.value = '2026-12-01';
    date.dispatchEvent(new Event('input', { bubbles: true }));
    dialog.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() =>
      expect(wrapper.get(settings).element.closest('article')?.textContent).toContain(
        'Last day of each month at 09:00 (UTC) · From 2026-12-01 · Off',
      ),
    );
  });

  it('edits and saves the Harvest batch on its card within the field limits', async () => {
    const { wrapper } = await renderAt(`${Afisha}/pipelines`);
    const field = 'input[aria-label="Batch size for Harvest"]';
    await vi.waitFor(() => expect(wrapper.find(field).exists()).toBe(true));
    const batch = wrapper.get<HTMLInputElement>(field);
    const original = batch.element.value;
    expect([batch.element.min, batch.element.max, batch.element.step]).toEqual(['1', '500', '1']);
    for (const invalid of ['0', '501', '1.5', '2234234234234234']) {
      await batch.setValue(invalid);
      expect(batch.element.value).toBe(original);
    }
    await batch.setValue('');
    await batch.trigger('blur');
    expect(batch.element.value).toBe(original);
    await batch.setValue('50');
    const form = batch.element.closest('form')!;
    expect(form.querySelector('button[type="submit"]')?.textContent).toContain('Apply');
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(form.querySelector('button[type="submit"]')).toBeNull());
    expect(batch.element.value).toBe('50');
  });

  it('shows a week of new posts by default and can show the full two-week series', async () => {
    const { wrapper } = await renderAt(Afisha);
    const panel = '[aria-label="New posts"]';
    await vi.waitFor(() => expect(wrapper.find(panel).exists()).toBe(true));
    expect(wrapper.get(panel).get('[role="group"]').findAll('button')).toHaveLength(7);
    await wrapper.get('button[aria-label="New posts period"]').trigger('click');
    const option = () =>
      Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
        (entry) => entry.textContent?.trim() === 'Last 14 days',
      );
    await vi.waitFor(() => expect(option()).toBeDefined());
    option()!.click();
    await vi.waitFor(() => expect(wrapper.get(panel).get('[role="group"]').findAll('button')).toHaveLength(14));
  });

  it('starts a stage from its source card without leaving the source page', async () => {
    const path = `${Afisha}/pipelines`;
    const { router, wrapper } = await renderAt(path);
    const run = 'button[aria-label="Run discover now"]';
    await vi.waitFor(() => expect(wrapper.get<HTMLButtonElement>(run).element.disabled).toBe(false));
    await wrapper.get(run).trigger('click');
    await vi.waitFor(() =>
      expect(wrapper.get(run).element.closest('article')?.textContent).toMatch(/queued|running|completed/i),
    );
    expect(router.currentRoute.value.path).toBe(path);
  });

  it('lets the operator disable an on-demand stage and pause its subscribed pipeline', async () => {
    const { router, wrapper } = await renderAt(`${FashionPipeline}/stages/process`);
    const stageButton = (label: string) => `button[aria-label="${label} process stage"]`;
    const run = 'button[aria-label="Run process now"]';
    await vi.waitFor(() => expect(wrapper.find(stageButton('Disable')).exists()).toBe(true));
    await wrapper.get(stageButton('Disable')).trigger('click');
    await vi.waitFor(() => expect(wrapper.find(stageButton('Enable')).exists()).toBe(true));
    expect(wrapper.get<HTMLButtonElement>(run).element.disabled).toBe(true);
    await wrapper.get(stageButton('Enable')).trigger('click');
    await vi.waitFor(() => expect(wrapper.get<HTMLButtonElement>(run).element.disabled).toBe(false));
    await router.push(FashionPipeline);
    const control = (label: string) => wrapper.findAll('button').find((button) => button.text() === label);
    await vi.waitFor(() => expect(control('Pause pipeline')).toBeDefined());
    await control('Pause pipeline')!.trigger('click');
    await vi.waitFor(() => expect(control('Resume pipeline')).toBeDefined());
    await control('Resume pipeline')!.trigger('click');
    await vi.waitFor(() => expect(control('Pause pipeline')).toBeDefined());
  });

  it('limits the Process batch when a model is selected', async () => {
    const { wrapper } = await renderAt(`${FashionPipeline}/stages/process`);
    const settings = 'button[aria-label="Schedule for Process"]';
    await vi.waitFor(() => expect(wrapper.find(settings).exists()).toBe(true));
    const batch = wrapper.get<HTMLInputElement>('input[aria-label="Batch size for Process"]');
    expect(batch.element.max).toBe('100');
    await batch.setValue('40');
    const form = batch.element.closest('form')!;
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(form.querySelector('button[type="submit"]')).toBeNull());
    await wrapper.get(settings).trigger('click');
    await vi.waitFor(() =>
      expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('Process schedule'),
    );
    const dialog = document.body.querySelector('[role="dialog"]')!;
    expect(dialog.querySelector('input[placeholder="Local processor"]')).toBeNull();
    (dialog.querySelector('button[aria-label="Processor"]') as HTMLButtonElement).click();
    const processor = () =>
      [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
        (item) => item.textContent?.trim() === 'Rental terms',
      );
    await vi.waitFor(() => expect(processor()).toBeDefined());
    processor()!.click();
    await flushPromises();
    const model = dialog.querySelector('input[placeholder="Local processor"]') as HTMLInputElement;
    model.value = 'test-model';
    model.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();
    expect(dialog.textContent).toContain('The model will run with a batch of 20 posts.');
    dialog.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(batch.element.max).toBe('20'));
    expect(batch.element.value).toBe('20');
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

describe('Available', () => {
  /** The sidebar's Available entry. */
  const entry = (wrapper: Awaited<ReturnType<typeof renderAt>>['wrapper']) =>
    wrapper.get('nav[aria-label="Console"] a[href="/available"]');
  const card = (wrapper: Awaited<ReturnType<typeof renderAt>>['wrapper'], name: string) =>
    wrapper.find(`article[aria-label="${name}"]`);

  it('lists the demo’s new and turned-off entries, counts them in the sidebar and leaves the turned-on ones out', async () => {
    const { wrapper } = await renderAt('/available');
    await vi.waitFor(() => expect(card(wrapper, 'BirBir').exists()).toBe(true));
    expect(wrapper.findAll('article')).toHaveLength(6);
    expect(wrapper.findAll('section h2').map((heading) => heading.text())).toEqual(['Events', 'Listings']);
    expect(card(wrapper, 'Afisha').exists()).toBe(false);
    expect(card(wrapper, 'Kultura').text()).toContain('Turned off');
    expect(card(wrapper, 'Kultura').get('a').attributes('href')).toMatch(/^\/sources\/[0-9a-f-]+$/);
    expect(entry(wrapper).text()).toContain('6');
  });

  it('puts a turned-on entry on Sources and takes it off Available, and the same for one that was turned off', async () => {
    const { router, wrapper } = await renderAt('/available');
    await vi.waitFor(() => expect(card(wrapper, 'Kultura').exists()).toBe(true));
    await wrapper.get('button[aria-label="Turn on BirBir"]').trigger('click');
    await vi.waitFor(() => expect(card(wrapper, 'BirBir').exists()).toBe(false));
    await vi.waitFor(() => expect(entry(wrapper).text()).toContain('5'));
    await wrapper.get('button[aria-label="Turn on Kultura"]').trigger('click');
    await vi.waitFor(() => expect(card(wrapper, 'Kultura').exists()).toBe(false));
    await vi.waitFor(() => expect(entry(wrapper).text()).toContain('4'));

    await router.push('/sources');
    await vi.waitFor(() => expect(wrapper.find('article[aria-label="birbir.uz"]').exists()).toBe(true));
    // Kultura.uz resumes as it was, which is disabled, so only the unfiltered page lists it.
    expect(wrapper.find('article[aria-label="Kultura.uz"]').exists()).toBe(false);
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Active only')!
      .trigger('click');
    await vi.waitFor(() => expect(wrapper.find('article[aria-label="Kultura.uz"]').exists()).toBe(true));
  });

  it('leaves a source its entry turned off out of Sources', async () => {
    const { wrapper } = await renderAt('/sources');
    await vi.waitFor(() => expect(wrapper.find('article[aria-label="Afisha.uz"]').exists()).toBe(true));
    expect(wrapper.find('article[aria-label="Kultura.uz"]').exists()).toBe(false);
    expect(wrapper.get('[aria-label="Sources"]').text()).toContain('of 12 sources');
  });

  it('turns an entry off from a source’s Settings, after a confirm, and opens Available', async () => {
    const { router, wrapper } = await renderAt(`${Afisha}/settings`);
    await vi.waitFor(() => expect(wrapper.text()).toContain('Turned on for this instance'));
    const control = () => wrapper.findAll('button').find((button) => button.text() === 'Turn off');
    await control()!.trigger('click');
    await vi.waitFor(() => expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull());
    const dialog = document.body.querySelector('[role="alertdialog"]')!;
    expect(dialog.textContent).toContain('Turn off Afisha?');
    for (const name of ['Afisha.uz', 'Afisha Telegram', 'Afisha Instagram']) expect(dialog.textContent).toContain(name);
    [...dialog.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Turn off')!.click();
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('available'));
    await vi.waitFor(() => expect(card(wrapper, 'Afisha').exists()).toBe(true));
    expect(card(wrapper, 'Afisha').text()).toContain('Turned off');
  });

  it('carries the Cards | Rows switch in the bar, sharing the remembered choice', async () => {
    useGalleryView().value = 'cards';
    const { wrapper } = await renderAt('/available');
    await vi.waitFor(() => expect(card(wrapper, 'BirBir').exists()).toBe(true));
    const bar = wrapper.get('[data-app-frame-bar]');
    expect(bar.find('[aria-label="View"]').exists()).toBe(true);
    expect(wrapper.get('[aria-label="Available sources"]').find('[aria-label="View"]').exists()).toBe(false);
    const rows = bar.findAll('[aria-label="View"] button').find((button) => button.text() === 'Rows')!;
    await rows.trigger('click');
    await vi.waitFor(() => expect(wrapper.find('li[aria-label="BirBir"]').exists()).toBe(true));
    expect(window.localStorage.getItem('scfd.gallery-view')).toBe('"rows"');
  });
});

describe('the Cards | Rows switch', () => {
  it('shows only on Sources, Available and a source’s Posts, with one remembered choice for all', async () => {
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
      ['/available', true],
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
    const fashionShowLink = () =>
      wrapper.findAll('a[data-post-link]').find((link) => link.text().includes('The Fashion Show Line 2026'));
    await vi.waitFor(() => expect(fashionShowLink()).toBeDefined());
    const link = fashionShowLink()!;
    expect(link.attributes('href')).toMatch(new RegExp(`^${Afisha}/posts/[0-9a-f-]+$`));
    await link.trigger('click');
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('source-post'));
    await vi.waitFor(() => expect(wrapper.find('[aria-label="Post"] h1').exists()).toBe(true));
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('nav[aria-label="Console"] [aria-current="page"]').text()).toContain('Afisha.uz');
    expect(wrapper.get('[data-app-frame-bar]').text()).toContain('Post');
    await vi.waitFor(() => expect(wrapper.get('[aria-label="Post"]').text()).toContain('Revision 2'));
    expect(wrapper.get('[aria-label="Capture"]').text()).toContain('First seen');
    expect(wrapper.get('[aria-label="Captured text"]').text()).toContain('Показ осенних коллекций');
    expect(wrapper.find('[aria-label="Processing results"]').exists()).toBe(false);
    expect(wrapper.get('[aria-label="Post"]').text()).not.toContain('Дворец творчества молодёжи');
  });
});
