import { defineComponent, h, shallowRef, type PropType } from 'vue';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { SourceOverviewModel, SourceProcessedPostDetailsModel, SourceProcessedPostModel } from '../src/contracts';
import ProcessedPostPanel from '../src/admin/harvestConsole/ProcessedPostPanel.vue';
import { createHarvestClient } from '../src/kit';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../src/admin/harvestConsole/HarvestConsole')>()),
  useHarvestConsole: () => session,
}));
const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

/** Renders a link with the place it names, so a test can read where it goes. */
const LinkStub = defineComponent({
  props: { to: { type: Object as PropType<Record<string, unknown>>, required: true } },
  setup(props, { slots }) {
    return () => h('a', { 'data-to': JSON.stringify(props.to) }, slots.default?.());
  },
});

const source = { sourceId: 'afisha', name: 'Afisha.uz', family: 'site' } as unknown as SourceOverviewModel;
const RunId = '1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed';
const Answered = '2026-11-19T09:00:00Z';

const runway: SourceProcessedPostModel = {
  postId: 'runway',
  title: 'Показ мод The Fashion Show Line 2026',
  url: 'https://www.afisha.uz/ru/fashion/1',
  revision: 2,
  firstSeenAt: new Date().toISOString(),
  state: 'live',
  language: 'ru',
  imageUrl: 'https://www.afisha.uz/media/runway.jpg',
  tags: {
    status: 'done',
    current: true,
    entityType: 'event',
    domains: [
      { key: 'style-fashion/womens-fashion', label: "Women's Fashion", primary: true },
      { key: 'style-fashion', label: 'Style & Fashion', primary: false },
      { key: 'retail', label: 'Retail', primary: false },
      { key: 'showrooms', label: 'Showrooms', primary: false },
      { key: 'autumn', label: 'Autumn', primary: false },
    ],
    topics: ['fashion-show', 'runway', 'autumn-collections', 'designers', 'tashkent', 'fashion'],
    about: 'a runway show of autumn collections',
    timeframe: 'upcoming',
    titleEn: 'Fashion show: The Fashion Show Line 2026',
    confidence: 0.94,
    flags: [{ field: 'timeframe', reason: 'the post states no date', proposed: 'upcoming' }],
    answer: {
      provider: 'anthropic',
      model: 'claude-haiku-5-5',
      promptVersion: 2,
      costMicroUsd: 300,
      latencyMs: 1840,
      inputTokens: 1240,
      outputTokens: 186,
      answeredAt: Answered,
    },
  },
  classification: {
    classifier: 'model:anthropic/claude-haiku-5-5@post-tags:2',
    verdict: 'relevant',
    confidence: 0.94,
    reason: 'event under style-fashion',
    kind: 'runway',
  },
  extraction: {
    useCase: 'extract-event',
    status: 'done',
    role: 'announcement',
    event: {
      name: 'The Fashion Show Line',
      editionLabel: '2026',
      occurrences: [{ starts: '2026-11-21', ends: '2026-11-23', precision: 'day', certainty: 'stated' }],
      venue: 'Youth Creativity Palace',
      city: 'Tashkent',
      country: 'UZ',
      format: 'runway',
      access: 'ticketed',
      prices: [{ amount: 150000, currency: 'UZS', label: 'entry' }],
    },
    flags: [{ field: 'occurrences[0].ends', reason: 'the post states no end', proposed: '2026-11-23' }],
    answer: {
      provider: 'anthropic',
      model: 'claude-haiku-5-5',
      promptVersion: 3,
      costMicroUsd: 1120,
      latencyMs: 2410,
      inputTokens: 2380,
      outputTokens: 412,
      answeredAt: '2026-11-19T10:00:00Z',
    },
  },
};

const runwayDetails: SourceProcessedPostDetailsModel = {
  post: runway,
  evidence: [
    { field: 'occurrences[0]', certainty: 'stated', quotes: ['The show runs on 21–23 November'] },
    { field: 'prices[0]', quotes: ["Tickets from 150 000 so'm"] },
    { field: 'registration.deadline', value: '2026-11-10', quotes: ['Register by 10 November'] },
  ],
  passes: [
    { pass: 'post-tags', status: 'done', inferenceId: RunId, answer: runway.tags.answer },
    { pass: 'extract-event', status: 'done', answer: runway.extraction!.answer },
  ],
};

/** A post the AI tagged as no entity: no verdict, no extraction, no url and no pass past the tags. */
const bare: SourceProcessedPostDetailsModel = {
  post: {
    postId: 'essay',
    title: 'A guide to the season’s fabrics',
    revision: 1,
    firstSeenAt: new Date().toISOString(),
    state: 'live',
    tags: {
      status: 'done',
      current: true,
      entityType: 'none',
      domains: [],
      topics: [],
      flags: [],
      answer: { provider: 'openai', model: 'gpt-5-mini', answeredAt: Answered },
    },
  },
  evidence: [],
  passes: [
    { pass: 'post-tags', status: 'done', answer: { provider: 'openai', model: 'gpt-5-mini', answeredAt: Answered } },
  ],
};

function open(details: SourceProcessedPostDetailsModel) {
  const read = vi.fn(async () => ResultExtensions.ok(details));
  const pageTitle = shallowRef<string>();
  session = { overviewOf: () => source, readSourceProcessedPost: read, pageTitle } as unknown as HarvestConsole;
  const wrapper = mount(ProcessedPostPanel, {
    props: { sourceId: 'afisha', postId: 'runway' },
    global: { stubs: { ConsoleLink: LinkStub } },
  });
  wrappers.push(wrapper);
  return { wrapper, read, pageTitle };
}

const sectionNames = (wrapper: ReturnType<typeof mount>) =>
  wrapper.findAll('section').map((section) => section.attributes('aria-label'));

describe('processed post page', () => {
  it('reads the post of its source and shows every section, the wide media header first', async () => {
    const { wrapper, read, pageTitle } = open(runwayDetails);
    expect(wrapper.text()).toContain('Reading the processed post…');
    await flushPromises();

    expect(read).toHaveBeenCalledWith('afisha', 'runway', expect.any(AbortSignal));
    expect(wrapper.text()).not.toContain('Reading the processed post…');
    expect(sectionNames(wrapper)).toEqual(['Classification', 'Tags', 'Event extraction', 'AI passes', 'Links']);
    const header = wrapper.get('header');
    expect(header.get('h1').text()).toBe(runway.title);
    expect(header.classes()).toEqual(expect.arrayContaining(['aspect-[16/9]', 'md:aspect-[21/8]']));
    expect(header.find('a[data-to]').exists()).toBe(false);
    expect(header.get('img').attributes('src')).toBe('https://www.afisha.uz/media/runway.jpg');
    expect(header.get('ul[aria-label="Entity type, verdict, language and AI provider"]').text()).toContain('Relevant');
    expect(header.get('p').text()).toContain('Fashion show: The Fashion Show Line 2026 · first seen');
    expect(header.get('a[target="_blank"]').attributes('href')).toBe('https://www.afisha.uz/ru/fashion/1');
    expect(header.get('[data-provider-pill]').attributes('aria-label')).toBe('Claude Haiku 5.5');
    expect(header.find('[class*="backdrop-blur-xl"]').exists()).toBe(true);
    expect(pageTitle.value).toBe(runway.title);
    expect(JSON.parse(wrapper.get('a[data-to]').attributes('data-to')!)).toEqual({
      kind: 'source',
      id: 'afisha',
      tab: 'processed',
    });
    expect(wrapper.get('a[data-to]').text()).toContain('Processed posts of Afisha.uz');
  });

  it('shows the classification: the verdict, its reason, confidence and classifier', async () => {
    const { wrapper } = open(runwayDetails);
    await flushPromises();
    const section = wrapper.get('[aria-label="Classification"]');
    expect(section.text()).toContain('Relevant');
    expect(section.text()).toContain('event under style-fashion');
    expect(section.text()).toContain('94%');
    expect(section.text()).toContain('runway');
    expect(section.text()).toContain('model:anthropic/claude-haiku-5-5@post-tags:2');
  });

  it('shows every tag with no count cut, the about line and an unsure tag with its reason and proposal', async () => {
    const { wrapper } = open(runwayDetails);
    await flushPromises();
    const section = wrapper.get('[aria-label="Tags"]');
    expect(section.text()).toContain('Event');
    expect(section.text()).toContain("Women's Fashion");
    expect(section.text()).toContain('style-fashion/womens-fashion');
    expect(section.findAll('ul[aria-label="Other domains"] li').map((item) => item.text())).toEqual([
      'Style & Fashion',
      'Retail',
      'Showrooms',
      'Autumn',
    ]);
    expect(section.findAll('ul[aria-label="Topics"] li').map((item) => item.text())).toEqual([
      '#fashion-show',
      '#runway',
      '#autumn-collections',
      '#designers',
      '#tashkent',
      '#fashion',
    ]);
    expect(section.text()).not.toMatch(/\+\d/);
    expect(section.text()).toContain('upcoming');
    expect(section.text()).toContain('a runway show of autumn collections');
    const flags = section.get('ul[aria-label="Unsure tags"]');
    expect(flags.text()).toContain('Timeframe');
    expect(flags.text()).toContain('the post states no date → upcoming');
  });

  it('shows each extracted field as a labelled row with its certainty, its flag inline and its evidence quote under it', async () => {
    const { wrapper } = open(runwayDetails);
    await flushPromises();
    const section = wrapper.get('[aria-label="Event extraction"]');
    expect(section.get('[data-role]').text()).toBe('announcement');
    expect(section.find('[data-status]').exists()).toBe(false);
    const rows = section.findAll('li[data-field]');
    expect(rows.map((row) => row.attributes('data-field'))).toEqual([
      'name',
      'occurrences[0]',
      'place',
      'format',
      'access',
      'prices[0]',
      'registration.deadline',
    ]);
    const dates = section.get('li[data-field="occurrences[0]"]');
    expect(dates.text()).toContain('Dates');
    expect(dates.text()).toContain('21–23 Nov');
    expect(dates.get('[data-certainty]').text()).toBe('stated');
    expect(dates.get('[data-flag="occurrences[0].ends"]').text()).toContain('the post states no end → 2026-11-23');
    const quote = dates.get('blockquote[data-evidence]');
    expect(quote.text()).toBe('“The show runs on 21–23 November”');
    expect(quote.classes()).toEqual(expect.arrayContaining(['italic', 'text-muted-foreground', 'border-s-2']));
    expect(section.get('li[data-field="prices[0]"]').text()).toContain('150,000 UZS entry');
    expect(section.get('li[data-field="prices[0]"] blockquote').text()).toContain("Tickets from 150 000 so'm");
    expect(section.get('li[data-field="registration.deadline"]').text()).toContain('2026-11-10');
    expect(section.get('li[data-field="registration.deadline"] blockquote').text()).toContain(
      'Register by 10 November',
    );
    expect(section.get('li[data-field="place"]').text()).toContain('Youth Creativity Palace, Tashkent, UZ');
    expect(section.get('li[data-field="place"]').find('blockquote').exists()).toBe(false);
  });

  it('lists each AI pass with its brand mark, model, cost, time, tokens in and out, answer time and run', async () => {
    const { wrapper } = open(runwayDetails);
    await flushPromises();
    const passes = wrapper.findAll('[aria-label="AI passes"] li[data-pass]');
    expect(passes.map((pass) => pass.attributes('data-pass'))).toEqual(['post-tags', 'extract-event']);
    const tags = passes[0]!;
    expect(tags.get('[data-pass-brand] [data-ai-brand]').attributes('data-ai-brand')).toBe('claude');
    expect(tags.text()).toContain('Tags');
    expect(tags.text()).toContain('Claude Haiku 5.5');
    expect(tags.text()).toContain('Cost$0.0003');
    expect(tags.text()).toContain('Time1.8s');
    expect(tags.text()).toContain('Tokens1,240 in · 186 out');
    expect(tags.text()).toMatch(/Answered.+ago/);
    expect(tags.text()).toContain('Promptp2');
    expect(tags.text()).toContain('Run1b9d6bcd');
    expect(tags.get('dd[title="' + RunId + '"]').text()).toBe('1b9d6bcd');
    const event = passes[1]!;
    expect(event.text()).toContain('Event extraction');
    expect(event.text()).toContain('Cost$0.0011');
    expect(event.text()).toContain('Tokens2,380 in · 412 out');
    expect(event.text()).not.toContain('Run');
  });

  it('links the captured post and the original', async () => {
    const { wrapper } = open(runwayDetails);
    await flushPromises();
    const links = wrapper.get('[aria-label="Links"]');
    const post = links.get('a[data-to]');
    expect(post.text()).toBe('Open post');
    expect(JSON.parse(post.attributes('data-to')!)).toEqual({
      kind: 'source',
      id: 'afisha',
      tab: 'posts',
      post: 'runway',
    });
    const original = links.get('a[target="_blank"]');
    expect(original.text()).toBe('Open original');
    expect(original.attributes('href')).toBe('https://www.afisha.uz/ru/fashion/1');
    expect(original.attributes('rel')).toBe('noopener noreferrer');
  });

  it('hides a section the post has no data for: no verdict, no extraction, no original, no later pass', async () => {
    const { wrapper } = open(bare);
    await flushPromises();
    expect(sectionNames(wrapper)).toEqual(['Tags', 'AI passes', 'Links']);
    expect(wrapper.find('[aria-label="Classification"]').exists()).toBe(false);
    expect(wrapper.find('[aria-label$="extraction"]').exists()).toBe(false);
    expect(wrapper.find('[aria-label="Links"] a[target="_blank"]').exists()).toBe(false);
    expect(wrapper.find('[aria-label="Links"] a[data-to]').exists()).toBe(true);
    expect(wrapper.findAll('li[data-pass]')).toHaveLength(1);
    expect(wrapper.find('ul[aria-label="Other domains"]').exists()).toBe(false);
    expect(wrapper.find('ul[aria-label="Topics"]').exists()).toBe(false);
    const header = wrapper.get('header');
    expect(header.classes()).toEqual(expect.arrayContaining(['min-h-32']));
    expect(header.find('img').exists()).toBe(false);

    const none = open({ ...bare, passes: [] });
    await flushPromises();
    expect(none.wrapper.find('[aria-label="AI passes"]').exists()).toBe(false);
  });

  it('shows an extraction that did not finish as a warning with its error and no field rows, and a failed tags answer as a line', async () => {
    const failedExtraction = {
      ...runwayDetails,
      post: {
        ...runway,
        extraction: {
          ...runway.extraction!,
          status: 'failed',
          error: 'no date found',
          event: undefined,
          role: undefined,
        },
      },
      evidence: [],
      passes: [
        runwayDetails.passes[0]!,
        { pass: 'extract-event', status: 'failed', error: 'no date found', answer: runway.extraction!.answer },
      ],
    } as SourceProcessedPostDetailsModel;
    const { wrapper } = open(failedExtraction);
    await flushPromises();
    const section = wrapper.get('[aria-label="Event extraction"]');
    expect(section.get('[data-status]').text()).toBe('failed');
    expect(section.text()).toContain('no date found');
    expect(section.find('li[data-field]').exists()).toBe(false);
    expect(section.get('ul[aria-label="Other unsure fields"]').text()).toContain('occurrences[0].ends');
    expect(wrapper.get('li[data-pass="extract-event"]').text()).toContain('failed');

    const tagsFailed = open({
      ...bare,
      post: { ...bare.post, tags: { ...bare.post.tags, status: 'failed', error: 'model unavailable' } },
    });
    await flushPromises();
    expect(tagsFailed.wrapper.get('[aria-label="Tags"] [role="status"]').text()).toBe(
      'Tagging failed · model unavailable',
    );
  });

  it('says it could not read the post, offers a retry and the captured post, and reads again', async () => {
    const read = vi
      .fn()
      .mockResolvedValueOnce(ResultExtensions.fail({ kind: 'offline' }))
      .mockResolvedValueOnce(ResultExtensions.ok(runwayDetails));
    session = {
      overviewOf: () => source,
      readSourceProcessedPost: read,
      pageTitle: shallowRef<string>(),
    } as unknown as HarvestConsole;
    const wrapper = mount(ProcessedPostPanel, {
      props: { sourceId: 'afisha', postId: 'runway' },
      global: { stubs: { ConsoleLink: LinkStub } },
    });
    wrappers.push(wrapper);
    await flushPromises();
    expect(wrapper.text()).toContain('Could not read this processed post');
    const post = wrapper.findAll('a[data-to]').find((link) => link.text() === 'Open the post')!;
    expect(JSON.parse(post.attributes('data-to')!)).toMatchObject({ tab: 'posts', post: 'runway' });
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Retry')!
      .trigger('click');
    await flushPromises();
    expect(read).toHaveBeenCalledTimes(2);
    expect(wrapper.get('h1').text()).toBe(runway.title);
  });

  it('names the page for the trail while it is open and clears it on leaving', async () => {
    const { wrapper, pageTitle } = open(runwayDetails);
    expect(pageTitle.value).toBeUndefined();
    await flushPromises();
    expect(pageTitle.value).toBe(runway.title);
    wrapper.unmount();
    expect(pageTitle.value).toBeUndefined();
  });
});

describe('processed post route', () => {
  it('reads one processed post from `api/sources/{id}/processed/{postId}` and refuses a malformed body', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
      const path = new URL(String(input), 'https://console.example/').pathname;
      return path.endsWith('/missing')
        ? Response.json({ title: 'Not found' }, { status: 404 })
        : path.endsWith('/broken')
          ? Response.json({ data: { post: {} } })
          : Response.json({ data: runwayDetails });
    });
    const api = createHarvestClient({ fetch });
    const found = await api.getSourceProcessedPost!('afisha', 'runway');
    expect(found.ok && found.value.evidence[0]?.quotes[0]).toBe('The show runs on 21–23 November');
    expect(found.ok && found.value.passes[0]?.inferenceId).toBe(RunId);
    expect(new URL(String(fetch.mock.calls[0]![0]), 'https://console.example/').pathname).toBe(
      '/api/sources/afisha/processed/runway',
    );
    expect((await api.getSourceProcessedPost!('afisha', 'missing')).ok).toBe(false);
    expect((await api.getSourceProcessedPost!('afisha', 'broken')).ok).toBe(false);
  });
});
