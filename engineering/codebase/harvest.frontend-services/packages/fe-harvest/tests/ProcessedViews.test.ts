import { defineComponent, h, type PropType } from 'vue';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { SourceOverviewModel, SourceProcessedPage, SourceProcessedPostModel } from '../src/contracts';
import { createHarvestClient } from '../src/kit';
import ChoicePicker from '../src/admin/harvestConsole/ChoicePicker.vue';
import SourceProcessedTab from '../src/admin/harvestConsole/SourceProcessedTab.vue';
import {
  answerLine,
  costText,
  eventLines,
  latencyText,
  listingLines,
  providerKindOf,
  provenanceLines,
  unsureFields,
} from '../src/admin/harvestConsole/ProcessedWords';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', () => ({ useHarvestConsole: () => session }));
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

const source = { sourceId: 'afisha', name: 'Afisha.uz' } as SourceOverviewModel;

const runway: SourceProcessedPostModel = {
  postId: 'runway',
  title: 'Показ мод The Fashion Show Line 2026',
  url: 'https://www.afisha.uz/ru/fashion/1',
  revision: 2,
  firstSeenAt: new Date().toISOString(),
  state: 'live',
  tags: {
    status: 'done',
    current: true,
    entityType: 'event',
    domains: [
      { key: 'style-fashion/womens-fashion', label: "Women's Fashion", primary: true },
      { key: 'style-fashion', label: 'Style & Fashion', primary: false },
    ],
    topics: ['fashion-show'],
    about: 'a runway show of autumn collections',
    timeframe: 'upcoming',
    titleEn: 'Fashion show: The Fashion Show Line 2026',
    confidence: 0.94,
    flags: [{ field: 'timeframe', reason: 'unsure', proposed: 'upcoming' }],
    answer: {
      provider: 'anthropic',
      model: 'claude-haiku-5-5',
      promptVersion: 2,
      costMicroUsd: 300,
      latencyMs: 1840,
      answeredAt: '2026-11-19T09:00:00Z',
    },
  },
  classification: { classifier: 'model:x', verdict: 'relevant', reason: 'event under style-fashion' },
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
    flags: [{ field: 'occurrences[0].ends', reason: 'unsure' }],
    answer: {
      provider: 'anthropic',
      model: 'claude-haiku-5-5',
      promptVersion: 3,
      costMicroUsd: 1120,
      latencyMs: 2410,
      answeredAt: '2026-11-19T09:00:00Z',
    },
  },
};
const essay: SourceProcessedPostModel = {
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
    answer: { provider: 'openai', model: 'gpt-5-mini', promptVersion: 1, answeredAt: '2026-11-19T09:00:00Z' },
  },
  classification: { classifier: 'model:y', verdict: 'irrelevant' },
};

function show(pages: SourceProcessedPage[]) {
  const query = vi.fn(async () => ResultExtensions.ok(pages.shift() ?? { items: [], total: 0 }));
  session = { querySourceProcessed: query } as unknown as HarvestConsole;
  const wrapper = mount(SourceProcessedTab, {
    props: { source },
    global: { stubs: { ConsoleLink: LinkStub } },
  });
  wrappers.push(wrapper);
  return { wrapper, query };
}

describe('processed words', () => {
  it('writes an answer as model, cost, latency and prompt in one compact line', () => {
    expect(costText(300)).toBe('$0.0003');
    expect(costText(12_000)).toBe('$0.012');
    expect(costText(1_250_000)).toBe('$1.25');
    expect(costText(0)).toBe('$0');
    expect(costText(undefined)).toBeUndefined();
    expect(latencyText(850)).toBe('850ms');
    expect(latencyText(1840)).toBe('1.8s');
    expect(answerLine(runway.tags.answer)).toBe('haiku-5-5 · $0.0003 · 1.8s · p2');
    expect(answerLine(essay.tags.answer)).toBe('gpt-5-mini · p1');
    expect(provenanceLines(runway.tags, runway.extraction)).toEqual([
      'tags haiku-5-5 · $0.0003 · 1.8s · p2',
      'event haiku-5-5 · $0.0011 · 2.4s · p3',
    ]);
    expect(providerKindOf('anthropic')).toBe('anthropic');
    expect(providerKindOf('OpenAI')).toBe('openai');
    expect(providerKindOf('mistral')).toBe('other');
    expect(unsureFields(runway.tags, runway.extraction)).toEqual(['timeframe', 'occurrences[0].ends']);
  });

  it('summarizes an event and a listing extraction in a few lines', () => {
    const now = new Date('2026-11-19T00:00:00Z');
    expect(eventLines(runway.extraction!.event!, now).map((line) => line.text)).toEqual([
      'The Fashion Show Line · 2026',
      '21–23 Nov · stated',
      'Youth Creativity Palace, Tashkent, UZ',
      'runway · ticketed · 150,000 UZS entry',
    ]);
    expect(
      listingLines({
        offerType: 'rent',
        price: { amount: 500, currency: 'USD', label: 'month' },
        rooms: 2,
        areaM2: 54,
        district: 'Chilonzor',
        city: 'Tashkent',
      }).map((line) => line.text),
    ).toEqual(['rent · 500 USD/month', '2 rooms · 54 m²', 'Chilonzor, Tashkent']);
  });
});

describe('processed tab', () => {
  it('shows each post with its verdict, tags, extraction summary and the provider as an icon', async () => {
    const { wrapper, query } = show([{ items: [runway, essay], total: 2 }]);
    await flushPromises();

    expect(query).toHaveBeenCalledWith('afisha', { limit: 30 }, expect.any(AbortSignal));
    expect(wrapper.text()).toContain('2 processed posts');
    const card = wrapper.get(`article[aria-label="${runway.title}"]`);
    expect(card.text()).toContain('Relevant');
    expect(card.text()).toContain('Event');
    expect(card.text()).toContain("Women's Fashion");
    expect(card.text()).toContain('Style & Fashion');
    expect(card.text()).toContain('#fashion-show');
    expect(card.text()).toContain('upcoming');
    expect(card.text()).toContain('a runway show of autumn collections');
    expect(card.text()).toContain('Fashion show: The Fashion Show Line 2026');
    expect(card.get('[aria-label="Event extraction"]').text()).toContain('announcement');
    expect(card.get('[data-line="place"]').text()).toBe('Youth Creativity Palace, Tashkent, UZ');
    expect(card.get('[data-line="terms"]').text()).toContain('150,000 UZS');
    const mark = card.get('[data-provider]');
    expect(mark.attributes('data-provider')).toBe('anthropic');
    expect(mark.attributes('aria-label')).toBe(
      'Anthropic: tags haiku-5-5 · $0.0003 · 1.8s · p2; event haiku-5-5 · $0.0011 · 2.4s · p3',
    );
    expect(card.get('[aria-label^="Unsure:"]').attributes('aria-label')).toBe('Unsure: timeframe, occurrences[0].ends');
    expect(JSON.parse(card.get('a[data-to]').attributes('data-to')!)).toEqual({
      kind: 'source',
      id: 'afisha',
      tab: 'posts',
      post: 'runway',
    });
    const other = wrapper.get(`article[aria-label="${essay.title}"]`);
    expect(other.get('[data-provider]').attributes('data-provider')).toBe('openai');
    expect(other.text()).toContain('Irrelevant');
    expect(other.find('[aria-label="Event extraction"]').exists()).toBe(false);
    expect(other.find('[aria-label^="Unsure:"]').exists()).toBe(false);
  });

  it('filters by type, verdict and extraction, and pages on with the cursor', async () => {
    const { wrapper, query } = show([
      { items: [runway], nextCursor: 'next', total: 2 },
      { items: [runway], nextCursor: 'more', total: 2 },
      { items: [essay], total: 2 },
    ]);
    await flushPromises();
    const [type, verdict, extracted] = wrapper.findAllComponents(ChoicePicker);
    type!.vm.$emit('update:modelValue', 'event');
    verdict!.vm.$emit('update:modelValue', 'relevant');
    extracted!.vm.$emit('update:modelValue', 'yes');
    await flushPromises();
    expect(query).toHaveBeenLastCalledWith(
      'afisha',
      { entityType: 'event', verdict: 'relevant', extracted: true, limit: 30 },
      expect.any(AbortSignal),
    );
    const more = wrapper.findAll('button').find((button) => button.text() === 'Show more')!;
    await more.trigger('click');
    await flushPromises();
    expect(query).toHaveBeenLastCalledWith(
      'afisha',
      { entityType: 'event', verdict: 'relevant', extracted: true, limit: 30, cursor: 'more' },
      expect.any(AbortSignal),
    );
    expect(wrapper.findAll('article').map((article) => article.attributes('aria-label'))).toEqual([
      runway.title,
      essay.title,
    ]);
    expect(wrapper.findAll('button').some((button) => button.text() === 'Show more')).toBe(false);
  });

  it('says when nothing is processed, and offers a retry when the read fails', async () => {
    const query = vi
      .fn()
      .mockResolvedValueOnce(ResultExtensions.fail({ kind: 'offline' }))
      .mockResolvedValueOnce(ResultExtensions.ok({ items: [], total: 0 }));
    session = { querySourceProcessed: query } as unknown as HarvestConsole;
    const wrapper = mount(SourceProcessedTab, { props: { source }, global: { stubs: { ConsoleLink: LinkStub } } });
    wrappers.push(wrapper);
    await flushPromises();
    expect(wrapper.text()).toContain("Could not load this source's processed posts.");
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Retry')!
      .trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('No processed posts yet.');
  });
});

describe('processed and entity routes', () => {
  it('reads processed posts and every entity route with their query strings', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
      const path = new URL(String(input), 'https://console.example/').pathname;
      const data = path.endsWith('/processed')
        ? { items: [runway], total: 1, nextCursor: 'c2' }
        : path.endsWith('/posts') || path.endsWith('/events')
          ? { data: [], next: 'n', hasMore: false }
          : path.endsWith('/calendar')
            ? []
            : path === '/api/entities'
              ? [{ id: 'e' }]
              : { id: 'e', name: 'Tashkent Fashion Week', years: [] };
      return Response.json({ data });
    });
    const api = createHarvestClient({ fetch });
    const page = await api.querySourceProcessed!('s', {
      entityType: 'event',
      verdict: 'relevant',
      extracted: false,
      limit: 30,
      cursor: 'c1',
    });
    expect(page.ok && page.value.items[0]?.postId).toBe('runway');
    const entities = api.entities!;
    expect((await entities.search('c', { q: ' visa ', type: 'event', limit: 100 })).ok).toBe(true);
    expect((await entities.get('c', 'e')).ok).toBe(true);
    expect((await entities.posts('c', 'e', { year: 2025, limit: 200, cursor: 'x' })).ok).toBe(true);
    expect((await entities.calendar('c', { from: '2026-11-19', to: '2027-11-19', type: 'event' })).ok).toBe(true);
    expect((await entities.events('c', { limit: 10 })).ok).toBe(true);
    const calls = fetch.mock.calls.map(([url]) => {
      const address = new URL(String(url), 'https://console.example/');
      return `${address.pathname}${address.search}`;
    });
    expect(calls).toEqual([
      '/api/sources/s/processed?entityType=event&verdict=relevant&extracted=false&limit=30&cursor=c1',
      '/api/entities?containerId=c&q=visa&type=event&limit=100',
      '/api/entities/e?containerId=c',
      '/api/entities/e/posts?containerId=c&year=2025&limit=200&cursor=x',
      '/api/entities/calendar?containerId=c&from=2026-11-19&to=2027-11-19&type=event',
      '/api/entities/events?containerId=c&limit=10',
    ]);
  });

  it('rejects a processed page without its tags, and an entity page without its cursor', async () => {
    const api = createHarvestClient({
      fetch: async (input) =>
        Response.json({
          data: String(input).includes('/processed')
            ? { items: [{ postId: 'p', title: 't' }], total: 1 }
            : { data: [{ postId: 'p' }], hasMore: false },
        }),
    });
    const page = await api.querySourceProcessed!('s', {});
    expect(!page.ok && page.failure.code).toBe('protocol');
    const posts = await api.entities!.posts('c', 'e');
    expect(!posts.ok && posts.failure.code).toBe('protocol');
  });
});
