import { defineComponent, h, nextTick, ref, type PropType } from 'vue';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { SourceOverviewModel, SourceProcessedPage, SourceProcessedPostModel } from '../src/contracts';
import { createHarvestClient } from '../src/kit';
import ChoicePicker from '../src/admin/harvestConsole/ChoicePicker.vue';
import SourceProcessedTab from '../src/admin/harvestConsole/SourceProcessedTab.vue';
import ProcessedPostCard from '../src/admin/harvestConsole/ProcessedPostCard.vue';
import ProcessedPostPanel from '../src/admin/harvestConsole/ProcessedPostPanel.vue';
import ProviderMark from '../src/admin/harvestConsole/ProviderMark.vue';
import {
  answerLine,
  certaintyTone,
  costText,
  eventLines,
  extractionRows,
  latencyText,
  listingLines,
  markLabel,
  provenanceLines,
  shortRunId,
  tagFlagLabel,
  tokenCount,
  tokensText,
  unsureFields,
  unsureFlags,
} from '../src/admin/harvestConsole/ProcessedWords';

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

const source = {
  sourceId: 'afisha',
  name: 'Afisha.uz',
  family: 'site',
  processedCount: 8,
  sections: [{ pipelineId: 'fashion' }],
} as unknown as SourceOverviewModel;

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
      inputTokens: 1240,
      outputTokens: 186,
      answeredAt: '2026-11-19T09:00:00Z',
    },
  },
  classification: {
    classifier: 'model:anthropic/claude-haiku-5-5@post-tags:2',
    verdict: 'relevant',
    confidence: 0.94,
    reason: 'event under style-fashion',
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

const showroom: SourceProcessedPostModel = {
  postId: 'showroom',
  title: 'Autumn collections at a showroom',
  revision: 1,
  firstSeenAt: new Date().toISOString(),
  state: 'live',
  language: 'uz',
  tags: {
    status: 'done',
    current: false,
    entityType: 'event',
    domains: [
      { key: 'style-fashion/designer-clothing', label: 'Designer Clothing', primary: true },
      { key: 'a', label: 'Style & Fashion', primary: false },
      { key: 'b', label: 'Retail', primary: false },
      { key: 'c', label: 'Showrooms', primary: false },
      { key: 'd', label: 'Autumn', primary: false },
    ],
    topics: ['showroom', 'autumn', 'collections', 'designers', 'tashkent', 'fashion'],
    flags: [],
    answer: {
      provider: 'google',
      model: 'gemini-2.5-flash',
      promptVersion: 2,
      costMicroUsd: 540,
      answeredAt: '2026-11-19T09:00:00Z',
    },
  },
  classification: { classifier: 'model:google/gemini-2.5-flash@post-tags:2', verdict: 'uncertain' },
  extraction: {
    useCase: 'extract-event',
    status: 'failed',
    error: 'no date found',
    flags: [],
    answer: { provider: 'openai', model: 'gpt-5-mini', promptVersion: 3, answeredAt: '2026-11-19T10:00:00Z' },
  },
};
const mystery: SourceProcessedPostModel = {
  postId: 'mystery',
  title: 'A note',
  revision: 1,
  firstSeenAt: new Date().toISOString(),
  state: 'gone',
  tags: {
    status: 'failed',
    error: 'model unavailable',
    current: true,
    domains: [],
    topics: [],
    flags: [],
    answer: { provider: 'acme', model: 'zeta-1', answeredAt: '2026-11-19T09:00:00Z' },
  },
};

const flat: SourceProcessedPostModel = {
  postId: 'flat',
  title: '2 xonali kvartira ijaraga',
  url: 'https://olx.example/flat',
  revision: 1,
  firstSeenAt: new Date().toISOString(),
  state: 'live',
  tags: {
    status: 'done',
    current: true,
    entityType: 'listing',
    domains: [{ key: 'real-estate', label: 'Real estate', primary: true }],
    topics: ['rent'],
    flags: [],
    answer: { provider: 'openai', model: 'gpt-5-mini', promptVersion: 4, answeredAt: '2026-11-19T09:00:00Z' },
  },
  classification: { classifier: 'model:openai/gpt-5-mini@post-tags:4', verdict: 'irrelevant' },
  extraction: {
    useCase: 'extract-listing',
    status: 'done',
    role: 'mention',
    listing: {
      offerType: 'rent',
      price: { amount: 500, currency: 'USD', label: 'month' },
      rooms: 2,
      areaM2: 54,
      district: 'Chilonzor',
      city: 'Tashkent',
    },
    flags: [],
    answer: { provider: 'openai', model: 'gpt-5-mini', promptVersion: 3, answeredAt: '2026-11-19T09:00:00Z' },
  },
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
    expect(answerLine(runway.tags.answer)).toBe('haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2');
    expect(answerLine(essay.tags.answer)).toBe('gpt-5-mini · p1');
    const now = new Date('2026-11-19T12:00:00Z');
    expect(provenanceLines(runway.tags, runway.extraction, now)).toEqual([
      'tags haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2',
      'event haiku-5-5 · $0.0011 · 2.4s · 2.8k tok · p3',
      'answered 3 hours ago',
    ]);
    expect(provenanceLines(showroom.tags, showroom.extraction, now).at(-1)).toBe('answered 2 hours ago');
    expect(unsureFields(runway.tags, runway.extraction)).toEqual(['timeframe', 'occurrences[0].ends']);
    expect(unsureFlags(runway.tags, runway.extraction)).toEqual([
      { field: 'timeframe', reason: 'unsure', proposed: 'upcoming' },
      { field: 'occurrences[0].ends', reason: 'the post states no end', proposed: '2026-11-23' },
    ]);
  });

  it('writes tokens as one short count, and names a model by its brand and label', () => {
    expect(tokenCount(186)).toBe('186');
    expect(tokenCount(1000)).toBe('1k');
    expect(tokenCount(1426)).toBe('1.4k');
    expect(tokenCount(12_400)).toBe('12k');
    expect(tokensText(runway.tags.answer)).toBe('1.4k tok');
    expect(tokensText({ outputTokens: 80, answeredAt: 'x' })).toBe('80 tok');
    expect(tokensText(essay.tags.answer)).toBeUndefined();
    expect(markLabel('anthropic', 'claude-haiku-5-5')).toBe('Claude Haiku 5.5');
    expect(markLabel('openai', 'gpt-5-mini')).toBe('OpenAI GPT-5 mini');
    expect(markLabel('google', 'gemini-2.5-flash')).toBe('Gemini 2.5 Flash');
    expect(markLabel('acme', 'zeta-1')).toBe('acme zeta-1');
    expect(markLabel(undefined, undefined)).toBe('Unknown provider');
  });

  it('builds the extraction rows: each field with its value, certainty, quotes and flags, stray flags apart', () => {
    const now = new Date('2026-11-19T00:00:00Z');
    const { rows, strayFlags } = extractionRows(
      {
        ...runway.extraction!,
        flags: [
          ...runway.extraction!.flags,
          { field: 'format', reason: 'value outside the list', proposed: 'gala' },
          { field: 'organizer', reason: 'unknown field' },
        ],
      },
      [
        { field: 'occurrences[0]', certainty: 'stated', quotes: ['Show on 21–23 November'] },
        { field: 'prices[0]', quotes: ["Tickets 150 000 so'm"] },
        { field: 'registration.deadline', value: '2026-11-10', quotes: ['register by 10 November'] },
      ],
      now,
    );
    expect(rows.map((row) => [row.key, row.label, row.value])).toEqual([
      ['name', 'Name', 'The Fashion Show Line · 2026'],
      ['occurrences[0]', 'Dates', '21–23 Nov'],
      ['place', 'Place', 'Youth Creativity Palace, Tashkent, UZ'],
      ['format', 'Format', 'runway'],
      ['access', 'Access', 'ticketed'],
      ['prices[0]', 'Price', '150,000 UZS entry'],
      ['registration.deadline', 'Registration deadline', '2026-11-10'],
    ]);
    const dates = rows[1]!;
    expect(dates.certainty).toBe('stated');
    expect(dates.quotes).toEqual(['Show on 21–23 November']);
    expect(dates.flags).toEqual([
      { field: 'occurrences[0].ends', reason: 'the post states no end', proposed: '2026-11-23' },
    ]);
    expect(rows[3]!.flags.map((flag) => flag.proposed)).toEqual(['gala']);
    expect(rows[5]!.quotes).toEqual(["Tickets 150 000 so'm"]);
    expect(rows[6]!.quotes).toEqual(['register by 10 November']);
    expect(strayFlags).toEqual([{ field: 'organizer', reason: 'unknown field' }]);
  });

  it('builds a listing’s rows with its rental terms from the evidence, and no rows for an unfinished pass', () => {
    const { rows } = extractionRows(flat.extraction!, [
      { field: 'price', quotes: ['500 USD per month'] },
      { field: 'rentalTerms.deposit', value: '500 USD', certainty: 'present', quotes: ['deposit 500 USD'] },
      { field: 'rentalTerms.utilities', certainty: 'absent', quotes: ['no utilities mentioned'] },
    ]);
    expect(rows.map((row) => [row.label, row.value])).toEqual([
      ['Offer', 'rent'],
      ['Price', '500 USD/month'],
      ['Rooms', '2'],
      ['Area', '54 m²'],
      ['Location', 'Chilonzor, Tashkent'],
      ['Deposit', '500 USD'],
      ['Utilities', 'absent'],
    ]);
    expect(rows[1]!.quotes).toEqual(['500 USD per month']);
    expect(rows[5]).toMatchObject({ certainty: 'present', quotes: ['deposit 500 USD'] });
    const failed = extractionRows(showroom.extraction!, []);
    expect(failed.rows).toEqual([]);
    expect(certaintyTone('estimated')).toBe('warning');
    expect(certaintyTone('stated')).toBe('success');
    expect(certaintyTone(undefined)).toBe('neutral');
    expect(shortRunId('1b9d6bcd-bbfd-4b2d-9b5d-ab8dfbbd4bed')).toBe('1b9d6bcd');
    expect(shortRunId(undefined)).toBeUndefined();
    expect(tagFlagLabel('timeframe')).toBe('Timeframe');
    expect(tagFlagLabel('x.y')).toBe('x.y');
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
  it('shows the processed title, type, extraction summary and answering brand in the gallery', async () => {
    const { wrapper, query } = show([{ items: [runway, essay], total: 2 }]);
    await flushPromises();

    expect(query).toHaveBeenCalledWith('afisha', { limit: 30 }, expect.any(AbortSignal));
    expect(wrapper.text()).not.toContain('processed posts');
    const card = wrapper.get('article:first-child');
    expect(card.get('header [data-processed-link]').text()).toBe(runway.tags.about);
    expect(card.get('[data-post-language]').text()).toBe('ru');
    expect(card.text()).not.toContain(runway.title);
    expect(card.text()).not.toContain('Relevant');
    expect(card.find('ul[aria-label="Tags"]').exists()).toBe(false);
    expect(card.get('[aria-label="Event extraction"]').text()).toContain('announcement');
    expect(card.get('[data-line="place"]').text()).toBe('Youth Creativity Palace, Tashkent, UZ');
    expect(card.get('[data-line="terms"]').text()).toContain('150,000 UZS');
    expect(card.get('[data-line="terms"] svg.lucide-runway-show').attributes('aria-hidden')).toBe('true');
    const mark = card.get('header [role="img"][aria-label="Claude Haiku 5.5"]');
    expect(mark.get('[data-ai-brand]').attributes('data-ai-brand')).toBe('claude');
    expect(card.find('footer').exists()).toBe(false);
    const unsure = card.get('[aria-label="Event extraction"] [aria-label^="2 unsure"]');
    expect(unsure.attributes('aria-label')).toBe('2 unsure: timeframe, occurrences[0].ends');
    expect(unsure.text()).toBe('? 2 unsure');
    expect(JSON.parse(card.get('a[data-to]').attributes('data-to')!)).toEqual({
      kind: 'source',
      id: 'afisha',
      processed: 'runway',
    });
    const other = wrapper.get('article:nth-child(2)');
    expect(other.get('header [data-ai-brand]').attributes('data-ai-brand')).toBe('openai');
    expect(other.text()).toContain('other post');
    expect(other.text()).not.toContain('Irrelevant');
    expect(other.find('[aria-label="Event extraction"]').exists()).toBe(false);
  });

  it('keeps the filter row free of a count while nothing narrows the list', async () => {
    const { wrapper } = show([{ items: [runway, essay], total: 2 }]);
    await flushPromises();
    const filters = wrapper.get('[role="group"][aria-label="Filter processed posts"]');
    const status = filters.get('[role="status"][aria-live="polite"]');
    expect(status.text()).toBe('');
    expect(filters.element.firstElementChild).toBe(status.element);
  });

  it('reads `x of y` first in the filter row while a filter narrows the list, `Updating…` while it reloads', async () => {
    let finish!: (page: SourceProcessedPage) => void;
    const query = vi
      .fn()
      .mockResolvedValueOnce(ResultExtensions.ok({ items: [runway, essay], total: 8 }))
      .mockImplementationOnce(() => new Promise((resolve) => (finish = (page) => resolve(ResultExtensions.ok(page)))))
      .mockResolvedValueOnce(ResultExtensions.ok({ items: [runway, essay], total: 8 }));
    session = { querySourceProcessed: query } as unknown as HarvestConsole;
    const wrapper = mount(SourceProcessedTab, { props: { source }, global: { stubs: { ConsoleLink: LinkStub } } });
    wrappers.push(wrapper);
    await flushPromises();
    const status = () => wrapper.get('[role="group"] [role="status"][aria-live="polite"]');
    expect(status().text()).toBe('');

    wrapper.findAllComponents(ChoicePicker)[1]!.vm.$emit('update:modelValue', 'relevant');
    await nextTick();
    expect(status().text()).toBe('Updating…');
    finish({ items: [runway], total: 1 });
    await flushPromises();
    expect(status().text()).toBe('1 of 8');

    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Clear filters')!
      .trigger('click');
    await flushPromises();
    expect(status().text()).toBe('');
  });

  it('reads only the match count when the host sends no source total', async () => {
    const query = vi.fn().mockResolvedValue(ResultExtensions.ok({ items: [runway], total: 1 }));
    session = { querySourceProcessed: query } as unknown as HarvestConsole;
    const wrapper = mount(SourceProcessedTab, {
      props: { source: { ...source, processedCount: undefined } },
      global: { stubs: { ConsoleLink: LinkStub } },
    });
    wrappers.push(wrapper);
    await flushPromises();
    wrapper.findAllComponents(ChoicePicker)[0]!.vm.$emit('update:modelValue', 'event');
    await flushPromises();
    expect(wrapper.get('[role="group"] [role="status"]').text()).toBe('1 matching');
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
    expect(wrapper.findAll('article [data-processed-link]').map((link) => link.text())).toEqual([
      runway.tags.about,
      'Untitled processed post',
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

describe('processed card', () => {
  const card = (post: SourceProcessedPostModel, extra: Record<string, unknown> = {}) => {
    session = {} as unknown as HarvestConsole;
    const wrapper = mount(ProcessedPostCard, {
      props: { source, post, ...extra },
      global: { stubs: { ConsoleLink: LinkStub } },
    });
    wrappers.push(wrapper);
    return wrapper;
  };

  it.each([
    ['trade-fair', 'trade-fair'],
    ['market', 'market-stall'],
    ['exhibition', 'exhibition-dress-form'],
    ['competition', 'competition-trophy'],
    ['festival', 'festival-bunting'],
    ['conference', 'conference-lectern'],
    ['other', 'other-event'],
  ])('shows the custom icon for the %s event format', (format, glyph) => {
    const extraction = runway.extraction!;
    const wrapper = card({ ...runway, extraction: { ...extraction, event: { ...extraction.event!, format } } });

    expect(wrapper.get(`[data-line="terms"] svg.lucide-${glyph}`).attributes('aria-hidden')).toBe('true');
  });

  it('uses the Posts card aspect ratio and puts the processed title over the image', () => {
    const wrapper = card(runway);
    const header = wrapper.get('header');
    expect(header.classes()).toContain('aspect-[4/5]');
    expect(header.get('img').attributes('src')).toBe('https://www.afisha.uz/media/runway.jpg');
    expect(header.get('img').attributes('alt')).toBe('');
    const pills = header.get('ul[aria-label="Processed post badges"]').findAll('[data-tone="glass"]');
    expect(pills.map((pill) => pill.text())).toEqual(['event', 'ru']);
    for (const pill of pills) expect(pill.classes()).toContain('bg-black/60');
    expect(header.find('[data-tone="glassSuccess"]').exists()).toBe(false);
    expect(header.get('a[data-to]').text()).toBe(runway.tags.about);
    expect(header.find('[class*="backdrop-blur-xl"]').exists()).toBe(true);
    expect(wrapper.find('[data-read-method]').exists()).toBe(false);
  });

  it('anchors the blur to the title block: a child of it, feathered 2rem above its top edge, no share of the header', () => {
    const wrapper = card(runway);
    const header = wrapper.get('header');
    const content = header.get('a[data-to]').element.parentElement!;
    const blur = header.get('[class*="backdrop-blur-xl"]');
    expect(blur.element.parentElement).toBe(content);
    expect(content.classList.contains('relative') && content.classList.contains('isolate')).toBe(true);
    expect(blur.classes()).toContain('-top-8');
    expect(blur.classes()).toContain('-inset-x-4');
    expect(blur.classes()).toContain('-bottom-4');
    for (const property of ['mask-image', '-webkit-mask-image']) {
      expect(blur.classes()).toContain(`[${property}:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)]`);
    }
    expect(blur.classes()).toEqual(expect.arrayContaining(['from-black/80', 'via-black/45']));
    expect(wrapper.html()).not.toContain('h-3/5');
    const bare = card(essay);
    expect(bare.find('[class*="backdrop-blur-xl"]').exists()).toBe(false);
    expect(bare.html()).not.toContain('h-3/5');
  });

  it('falls back to a source-icon tile with quiet pills for a post without an image, or one whose image fails', async () => {
    const wrapper = card(essay);
    const header = wrapper.get('header');
    expect(header.classes()).toContain('aspect-[4/5]');
    expect(header.find('img').exists()).toBe(false);
    expect(header.find('[data-read-method="site"]').exists()).toBe(true);
    expect(header.find('[data-tone="glass"]').exists()).toBe(false);
    expect(header.get('[data-tone="outline"]').text()).toBe('other post');
    expect(header.get('[data-processed-link]').text()).toBe('Untitled processed post');
    expect(header.get('[data-processed-link]').text()).not.toBe(essay.title);

    const failing = card(runway);
    await failing.get('header img').trigger('error');
    expect(failing.get('header').classes()).toContain('aspect-[4/5]');
    expect(failing.find('header img').exists()).toBe(false);
  });

  it('shows a post removed from its source', () => {
    const gone = card(mystery);
    expect(gone.get('[data-post-removed]').text()).toBe('removed');
  });

  it('opens the original from the Posts card’s glass ↗ button top right, only for a safe web address', async () => {
    const wrapper = card(runway);
    const open = wrapper.get('header a[target="_blank"]');
    expect(open.attributes('href')).toBe('https://www.afisha.uz/ru/fashion/1');
    expect(open.attributes('rel')).toBe('noopener noreferrer');
    expect(open.attributes('aria-label')).toBe('Open the original post');
    expect(open.classes()).toEqual(
      expect.arrayContaining([
        'absolute',
        'top-3',
        'right-3',
        'z-20',
        'size-8',
        'rounded-full',
        'border-white/20',
        'bg-black/35',
        'text-white',
        'backdrop-blur-md',
      ]),
    );
    expect(open.get('svg path').attributes('d')).toBe('M7 17 17 7M8 7h9v9');
    await open.trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('Open the original');
    });
    const suspect = card({ ...runway, state: 'suspect' });
    await suspect.get('header a[target="_blank"]').trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('could not confirm it is still there');
    });

    for (const url of [undefined, 'javascript:alert(1)', 'ftp://files.example/a', 'https://user:pw@www.afisha.uz/a'])
      expect(
        card({ ...runway, url })
          .find('header a[target="_blank"]')
          .exists(),
      ).toBe(false);
    expect(
      card({ ...runway, state: 'gone' })
        .find('header a[target="_blank"]')
        .exists(),
    ).toBe(false);
  });

  it('puts the AI provider mark last in the top pill row as a round glass pill, no longer top right', () => {
    const wrapper = card(runway);
    const row = wrapper.get('ul[aria-label="Processed post badges"]');
    const mark = row.findAll('li').at(-1)!.get('[data-provider-pill]');
    expect(mark.attributes('aria-label')).toBe('Claude Haiku 5.5');
    expect(mark.classes()).toEqual(
      expect.arrayContaining([
        'size-6',
        'rounded-full',
        'border-white/20',
        'bg-black/60',
        'text-white',
        'backdrop-blur-md',
      ]),
    );
    expect(mark.get('[data-ai-brand]').attributes('data-ai-brand')).toBe('claude');
    expect(wrapper.findAll('[role="img"][aria-label="Claude Haiku 5.5"]')).toHaveLength(1);
    expect(wrapper.get('header').find('.end-3').exists()).toBe(false);
    expect(card(essay).get('[data-provider-pill]').classes()).toEqual(
      expect.arrayContaining(['border-border', 'bg-card']),
    );
    expect(row.findAll('li')).toHaveLength(3);
  });

  it('keeps the pass-by-pass hover on the provider pill: each pass with its model, cost, time and tokens', async () => {
    const wrapper = card(runway);
    await wrapper.get('[data-provider-pill]').trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('tags haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2');
    });
    expect(document.body.textContent).toContain('event haiku-5-5 · $0.0011 · 2.4s · 2.8k tok · p3');
    expect(document.body.textContent).toMatch(/answered .+ ago/);
  });

  it('names the extraction by its role alone: no Event or Listing word, no done', () => {
    const event = card(runway).get('[aria-label="Event extraction"] p');
    expect(event.get('span').text()).toBe('announcement');
    expect(event.text()).not.toMatch(/Event|done/);
    const listing = card(flat).get('[aria-label="Listing extraction"]');
    expect(listing.get('p').text()).toBe('mention');
    expect(listing.text()).not.toMatch(/Listing|done/);
    expect(listing.get('[data-line="offer"]').text()).toBe('rent · 500 USD/month');
    const failed = card(showroom).get('[aria-label="Event extraction"] p');
    expect(failed.get('[data-tone="warning"]').text()).toBe('failed');
    expect(failed.text()).toContain('no date found');
    expect(failed.text()).not.toMatch(/Event|done/);
    const bare = card({
      ...runway,
      tags: { ...runway.tags, flags: [] },
      extraction: { ...runway.extraction!, role: undefined, event: undefined, flags: [] },
    });
    expect(bare.find('[aria-label="Event extraction"]').exists()).toBe(false);
  });

  it('puts Yandex and Google links after the place line, searching the full place text, and none without a place', () => {
    const place = 'Youth Creativity Palace, Tashkent, UZ';
    const wrapper = card(runway);
    const links = wrapper.findAll('[data-line="place"] a[data-map-provider]');
    expect(links.map((link) => link.attributes('data-map-provider'))).toEqual(['yandex', 'google']);
    for (const link of links) expect(link.attributes('href')).toContain(encodeURIComponent(place));
    const clicked = vi.fn();
    wrapper.element.addEventListener('click', clicked);
    links[0]!.element.addEventListener('click', (event) => event.preventDefault());
    links[0]!.trigger('click');
    expect(clicked).not.toHaveBeenCalled();
    const bare = card({
      ...runway,
      extraction: {
        ...runway.extraction!,
        event: { ...runway.extraction!.event!, venue: undefined, city: undefined, country: undefined },
      },
    });
    expect(bare.find('[data-map-provider]').exists()).toBe(false);
  });

  it('opens the processed post’s page from the whole card: the title link stretches over it, the controls stay above', () => {
    const wrapper = card(runway);
    const link = wrapper.get('a[data-processed-link]');
    expect(JSON.parse(link.attributes('data-to')!)).toEqual({ kind: 'source', id: 'afisha', processed: 'runway' });
    expect(link.text()).toBe(runway.tags.about);
    expect(link.classes()).toEqual(
      expect.arrayContaining(['after:absolute', 'after:-inset-[100rem]', "after:content-['']"]),
    );
    expect(wrapper.findAll('a[data-to]')).toHaveLength(1);
    expect(wrapper.get('article').classes()).toEqual(expect.arrayContaining(['isolate', 'overflow-hidden']));
    expect(wrapper.get('header').classes()).not.toContain('overflow-hidden');
    expect(wrapper.get('ul[aria-label="Processed post badges"]').classes()).toContain('z-10');
    expect(wrapper.get('a[target="_blank"]').classes()).toContain('z-20');
    expect(wrapper.get('[aria-label^="2 unsure"]').classes()).toEqual(expect.arrayContaining(['relative', 'z-10']));
    expect(JSON.parse(card(flat).get('a[data-processed-link]').attributes('data-to')!)).toMatchObject({
      processed: 'flat',
    });
  });

  it('shows the two post ages beneath the title and the event date in the extraction', () => {
    const extraction = runway.extraction!;
    const now = Date.now();
    const postedAt = new Date(now - 16 * 60 * 60_000).toISOString();
    const firstSeenAt = new Date(now - 8 * 60 * 60_000).toISOString();
    const wrapper = card({
      ...runway,
      postedAt,
      firstSeenAt,
      extraction: { ...extraction, event: { ...extraction.event!, languages: ['uz', 'en'] } },
    });
    const pills = wrapper.get('ul[aria-label="Processed post badges"]');
    expect(pills.findAll('li').map((item) => item.text())).toEqual(['event', 'ru', '']);
    expect(pills.get('[data-post-language]').attributes('aria-label')).toBe('Russian');
    const metadata = wrapper.get('[data-post-metadata]');
    const [posted, harvested] = metadata.findAll('[data-post-age]');
    expect(posted!.text()).toBe('16 h');
    expect(posted!.attributes('aria-label')).toBe('Posted 16 hours ago');
    expect(posted!.get('time').attributes('datetime')).toBe(postedAt);
    expect(posted!.get('svg').classes()).toContain('lucide-file-up-icon');
    expect(harvested!.text()).toBe('8 h');
    expect(harvested!.attributes('aria-label')).toBe('Harvested 8 hours ago');
    expect(harvested!.get('time').attributes('datetime')).toBe(firstSeenAt);
    expect(harvested!.get('svg').classes()).toContain('lucide-import-icon');
    expect(metadata.find('[data-post-language]').exists()).toBe(false);
    expect(wrapper.get('[data-line="dates"] svg').classes()).toContain('lucide-calendar-icon');
    expect(wrapper.get('[data-line="languages"]').text()).toBe('Event language · Uzbek, English');
    expect(card(showroom).get('[data-post-language]').text()).toBe('uz');
    expect(card(essay).find('[data-post-language]').exists()).toBe(false);
    expect(card(essay).find('[data-post-age="posted"]').exists()).toBe(false);
    expect(
      card({ ...essay, language: 'und' })
        .find('[data-post-language]')
        .exists(),
    ).toBe(false);
    expect(card(runway).find('[data-line="languages"]').exists()).toBe(false);
  });

  it('shows only the extraction summary below the media and omits the body for a tag-only post', () => {
    const wrapper = card(runway);
    const body = wrapper.get('[aria-label="Event extraction"]');
    expect([...wrapper.element.children]).toEqual([wrapper.get('header').element, body.element]);
    expect(wrapper.find('ul[aria-label="Tags"]').exists()).toBe(false);
    expect(wrapper.text()).not.toContain('#fashion-show');
    expect(body.text()).not.toContain(runway.tags.about);
    expect(card(essay).element.children).toHaveLength(1);
  });

  it('puts a tagging failure in the media pills and an extraction failure in the body', async () => {
    const failed = card(mystery);
    const pill = failed.get('header [data-tone="outline"][tabindex="0"]');
    expect(pill.text()).toBe('tagging failed');
    await pill.trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('Tagging failed · model unavailable');
    });
    expect(failed.element.children).toHaveLength(1);
    const wrapper = card(showroom);
    const panel = wrapper.get('[aria-label="Event extraction"]');
    expect(panel.get('[data-tone="warning"]').text()).toBe('failed');
    expect(panel.text()).toContain('no date found');
    expect(panel.find('[data-line]').exists()).toBe(false);
  });

  it('explains other posts and shows each unsure field with its reason and proposal', async () => {
    const other = card(essay).get('header [data-tone="outline"][tabindex="0"]');
    await other.trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('The tagging pass found no event or listing in this post.');
    });
    const wrapper = card(runway);
    const unsure = wrapper.get('[aria-label="Event extraction"] [aria-label^="2 unsure"]');
    await unsure.trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('the post states no end → 2026-11-23');
    });
    expect(document.body.textContent).toContain('timeframe');
  });

  it('draws the brand of each provider: Claude, OpenAI, Gemini, and a neutral chip for an unknown one', () => {
    const brands = [runway, essay, showroom, mystery].map((post) =>
      card(post).get('header [data-ai-brand]').attributes('data-ai-brand'),
    );
    expect(brands).toEqual(['claude', 'openai', 'gemini', 'unknown']);
    expect(card(mystery).get('header [role="img"]').attributes('aria-label')).toBe('acme zeta-1');
  });

  it('keeps the AI stats out of the card body: no footer and no per-pass text', () => {
    for (const post of [runway, essay, showroom, mystery]) {
      const wrapper = card(post);
      expect(wrapper.find('footer').exists()).toBe(false);
      expect(wrapper.findAll('time').map((time) => time.attributes('datetime'))).toEqual(
        [post.postedAt, post.firstSeenAt].filter(Boolean),
      );
      expect(wrapper.find('[data-pass]').exists()).toBe(false);
      expect(wrapper.text()).not.toMatch(/\$0\.|\btok\b|\d(\.\d)?m?s\b|Claude Haiku|answered/);
    }
    expect(card(runway).findAll('[data-ai-brand]')).toHaveLength(1);
  });

  it('hands the provider mark each pass with its cost, time and tokens, and when it answered', () => {
    const lines = card(runway).getComponent(ProviderMark).props('lines');
    expect(lines.slice(0, 2)).toEqual([
      'tags haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2',
      'event haiku-5-5 · $0.0011 · 2.4s · 2.8k tok · p3',
    ]);
    expect(lines).toHaveLength(3);
    expect(lines[2]).toMatch(/^answered .+ ago$/);
    expect(card(showroom).getComponent(ProviderMark).props('lines')).toHaveLength(3);
    expect(card(essay).getComponent(ProviderMark).props('lines')[0]).toBe('tags gpt-5-mini · p1');
  });

  it('shows the answered time and every pass in the mark tooltip on hover or focus', async () => {
    const wrapper = card(runway);
    await wrapper.get('header [role="img"][aria-label="Claude Haiku 5.5"]').trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('event haiku-5-5 · $0.0011 · 2.4s · 2.8k tok · p3');
    });
    expect(document.body.textContent).toContain('tags haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2');
    expect(document.body.textContent).toMatch(/answered .+ ago/);
  });

  it('keeps the unsure pill, with its flags, at the end of the extraction summary top row', () => {
    const wrapper = card(runway);
    const row = wrapper.get('[aria-label="Event extraction"] p');
    expect(row.get('[aria-label^="2 unsure"]').text()).toBe('? 2 unsure');
    expect(row.get('[aria-label^="2 unsure"]').classes()).toContain('ms-auto');
    expect(card(essay).find('[aria-label*="unsure"]').exists()).toBe(false);
    expect(card(showroom).find('[aria-label*="unsure"]').exists()).toBe(false);
  });

  it('lists the model, cost, latency, tokens and prompt in the mark tooltip', async () => {
    const mark = mount(ProviderMark, {
      props: { provider: 'google', model: 'gemini-2.5-flash', lines: ['tags gemini-2.5-flash · $0.0005 · p2'] },
    });
    wrappers.push(mark);
    expect(mark.get('[role="img"]').attributes('aria-label')).toBe('Gemini 2.5 Flash');
    await mark.get('[role="img"]').trigger('focus');
    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('tags gemini-2.5-flash · $0.0005 · p2');
    });
  });
});

describe('processed post kind icons', () => {
  async function panel(post: SourceProcessedPostModel) {
    session = {
      overviewOf: () => source,
      pageTitle: ref<string>(),
      readSourceProcessedPost: async () => ResultExtensions.ok({ post, evidence: [], passes: [] }),
      querySourcePosts: async () => ResultExtensions.ok({ items: [], total: 0 }),
    } as unknown as HarvestConsole;
    const wrapper = mount(ProcessedPostPanel, {
      props: { sourceId: source.sourceId, postId: post.postId },
      global: { stubs: { ConsoleLink: LinkStub } },
    });
    wrappers.push(wrapper);
    await flushPromises();
    return wrapper;
  }

  it('uses the classifier kind and the extracted format independently', async () => {
    const extraction = runway.extraction!;
    const wrapper = await panel({
      ...runway,
      classification: { ...runway.classification!, kind: 'trade-fair' },
      extraction: { ...extraction, event: { ...extraction.event!, format: 'market' } },
    });

    expect(wrapper.find('[aria-label="Classification"] svg.lucide-trade-fair').exists()).toBe(true);
    expect(wrapper.find('[data-field="format"] svg.lucide-market-stall').exists()).toBe(true);
  });

  it('leaves an unrecognized event format and a non-event Other classification without event glyphs', async () => {
    const extraction = runway.extraction!;
    const event = await panel({
      ...runway,
      extraction: { ...extraction, event: { ...extraction.event!, format: 'workshop' } },
    });
    expect(event.get('[data-field="format"]').find('svg').exists()).toBe(false);

    const listing = await panel({ ...flat, classification: { ...runway.classification!, kind: 'other' } });
    expect(listing.get('[aria-label="Classification"]').find('svg').exists()).toBe(false);
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
