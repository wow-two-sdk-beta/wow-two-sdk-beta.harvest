import { defineComponent, h, ref, type PropType } from 'vue';
import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiFailureFactory } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { EntityPostModel, EntitySummaryModel, EntityTimelineModel } from '../src/contracts';
import ChoicePicker from '../src/admin/harvestConsole/ChoicePicker.vue';
import EntitiesPanel from '../src/admin/harvestConsole/EntitiesPanel.vue';
import EntityPanel from '../src/admin/harvestConsole/EntityPanel.vue';
import { cadenceWords, domainWords, occurrenceDates, roleWords } from '../src/admin/harvestConsole/EntityWords';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', () => ({ useHarvestConsole: () => session }));
const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

const LinkStub = defineComponent({
  props: { to: { type: Object as PropType<Record<string, unknown>>, required: true } },
  setup(props, { slots }) {
    return () => h('a', { 'data-to': JSON.stringify(props.to) }, slots.default?.());
  },
});

const week: EntitySummaryModel = {
  id: 'week',
  containerId: 'pool',
  entityType: 'event',
  name: 'Tashkent Fashion Week',
  aliases: ['Ташкентская неделя моды'],
  primaryDomain: 'style-fashion/womens-fashion',
  status: 'active',
  cadence: 'annual',
  years: [
    { year: 2025, occurrences: 1, posts: 2 },
    { year: 2026, occurrences: 1, posts: 1 },
    { year: 2027, occurrences: 0, predicted: true, posts: 0 },
  ],
  predicted: '2027-11-21',
  occurrences: 2,
  posts: 3,
  sources: 2,
  lastSeenAtUtc: '2026-11-19T09:00:00Z',
};
const flatSummary: EntitySummaryModel = {
  id: 'flat',
  containerId: 'pool',
  entityType: 'listing',
  name: '2-room flat, Chilonzor 9',
  aliases: [],
  primaryDomain: 'real-estate/apartments',
  status: 'active',
  cadence: 'unknown',
  years: [{ year: 2024, occurrences: 0, posts: 1 }],
  occurrences: 0,
  posts: 1,
  sources: 1,
  lastSeenAtUtc: '2026-11-19T09:00:00Z',
};

describe('entity words', () => {
  it('writes dates at their precision, cadence, domain and roles', () => {
    expect(occurrenceDates({ starts: '2026-10-01', precision: 'month' })).toBe('Oct 2026');
    expect(occurrenceDates({ starts: '2027-01-01', precision: 'year' })).toBe('2027');
    expect(
      occurrenceDates({ starts: '2026-11-21', ends: '2026-11-23', precision: 'day' }, new Date('2026-01-01')),
    ).toBe('21–23 Nov');
    expect(cadenceWords('annual')).toBe('Every year');
    expect(domainWords('style-fashion/womens-fashion')).toBe('Womens fashion');
    expect(roleWords({ role: 'update', changes: ['dates', 'place'] })).toBe('update: dates, place');
  });
});

describe('entities page', () => {
  function show() {
    const search = vi.fn(async () => ResultExtensions.ok([week, flatSummary]));
    const calendar = vi.fn(async () =>
      ResultExtensions.ok([
        {
          entityId: 'week',
          entityType: 'event',
          name: 'Tashkent Fashion Week',
          cadence: 'annual',
          posts: 1,
          occurrence: {
            id: 'o1',
            label: '2026',
            starts: '2026-11-21',
            ends: '2026-11-23',
            precision: 'day',
            certainty: 'stated',
            status: 'scheduled',
            place: { city: 'Tashkent' },
          },
        },
        {
          entityId: 'week',
          entityType: 'event',
          name: 'Tashkent Fashion Week',
          cadence: 'annual',
          posts: 0,
          occurrence: {
            id: 'o2',
            label: '2027',
            starts: '2027-11-21',
            precision: 'day',
            certainty: 'estimated',
            predicted: true,
            status: 'scheduled',
          },
        },
      ]),
    );
    session = {
      containerId: ref('pool'),
      searchEntities: search,
      readEntityCalendar: calendar,
    } as unknown as HarvestConsole;
    const wrapper = mount(EntitiesPanel, { global: { stubs: { ConsoleLink: LinkStub } } });
    wrappers.push(wrapper);
    return { wrapper, search, calendar };
  }

  it('lists the entities with their years, next run and counts, and filters by type and year', async () => {
    vi.useFakeTimers();
    const { wrapper, search } = show();
    await flushPromises();
    expect(search).toHaveBeenCalledWith({ limit: 100 }, expect.any(AbortSignal));
    const rows = wrapper.findAll('[aria-label="Entities"] > li');
    expect(rows).toHaveLength(2);
    expect(rows[0]!.text()).toContain('Tashkent Fashion Week');
    expect(rows[0]!.text()).toContain('Every year');
    expect(rows[0]!.text()).toContain('Womens fashion');
    expect(rows[0]!.text()).toContain('2 runs · 3 posts · 2 sources');
    expect(rows[0]!.text()).toMatch(/next 21 Nov 2027/);
    expect(JSON.parse(rows[0]!.get('a[data-to]').attributes('data-to')!)).toEqual({ kind: 'entity', id: 'week' });

    const [type, year] = wrapper.findAllComponents(ChoicePicker);
    year!.vm.$emit('update:modelValue', '2024');
    await flushPromises();
    expect(wrapper.findAll('[aria-label="Entities"] > li').map((row) => row.find('a').text())).toEqual([
      '2-room flat, Chilonzor 9',
    ]);
    type!.vm.$emit('update:modelValue', 'event');
    await flushPromises();
    expect(search).toHaveBeenLastCalledWith({ type: 'event', limit: 100 }, expect.any(AbortSignal));

    await wrapper.get('input').setValue('  visa ');
    await vi.advanceTimersByTimeAsync(300);
    await flushPromises();
    expect(search).toHaveBeenLastCalledWith({ q: 'visa', type: 'event', limit: 100 }, expect.any(AbortSignal));
    vi.useRealTimers();
  });

  it('shows the calendar by month with the predicted run marked', async () => {
    const { wrapper, calendar } = show();
    await flushPromises();
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Calendar')!
      .trigger('click');
    await flushPromises();
    expect(calendar).toHaveBeenCalledWith(
      { from: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), to: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) },
      expect.any(AbortSignal),
    );
    expect(wrapper.get('[aria-label="November 2026"]').text()).toContain('Tashkent');
    expect(wrapper.get('[aria-label="November 2026"]').text()).toContain('scheduled');
    expect(wrapper.get('[aria-label="November 2027"]').text()).toContain('predicted');
  });
});

describe('entity page', () => {
  const timeline: EntityTimelineModel = {
    id: 'week',
    containerId: 'pool',
    entityType: 'event',
    name: 'Tashkent Fashion Week',
    aliases: ['Ташкентская неделя моды', 'Toshkent moda haftaligi'],
    primaryDomain: 'style-fashion/womens-fashion',
    status: 'active',
    cadence: 'annual',
    cadenceConfidence: 0.86,
    firstSeenAtUtc: '2025-10-01T09:00:00Z',
    lastSeenAtUtc: '2026-11-19T09:00:00Z',
    posts: 3,
    sources: 2,
    years: [
      {
        year: 2025,
        posts: 1,
        occurrences: [
          {
            id: 'o25',
            label: '2025',
            starts: '2025-11-24',
            ends: '2025-11-26',
            precision: 'day',
            certainty: 'stated',
            status: 'held',
            place: { venue: 'Hilton', city: 'Tashkent', country: 'UZ' },
          },
        ],
      },
      {
        year: 2027,
        posts: 0,
        occurrences: [
          {
            id: 'o27',
            label: '2027',
            starts: '2027-11-21',
            precision: 'day',
            certainty: 'estimated',
            predicted: true,
            status: 'scheduled',
          },
        ],
      },
    ],
  };
  const posts: EntityPostModel[] = [
    {
      postId: 'p1',
      role: 'update',
      changes: ['place'],
      year: 2026,
      title: 'Fashion week moves to the Youth Palace',
      sourceId: 'telegram',
      source: 'Afisha Telegram',
      postedAt: '2026-11-07',
      firstSeenAtUtc: '2026-11-07T09:00:00Z',
      boundAtUtc: '2026-11-07T09:05:00Z',
    },
    {
      postId: 'p2',
      role: 'report',
      changes: [],
      year: 2025,
      title: 'Fashion week: the collections',
      sourceId: 'afisha',
      source: 'Afisha.uz',
      postedAt: '2025-11-27',
      firstSeenAtUtc: '2025-11-27T09:00:00Z',
      boundAtUtc: '2025-11-27T09:05:00Z',
    },
  ];

  it('shows the names, cadence and next run, then each year with its runs and its posts by role', async () => {
    const read = vi.fn(async () => ResultExtensions.ok(timeline));
    const page = vi.fn(async () => ResultExtensions.ok({ data: posts, next: 'n', hasMore: false }));
    session = { containerId: ref('pool'), readEntity: read, readEntityPosts: page } as unknown as HarvestConsole;
    const wrapper = mount(EntityPanel, { props: { entityId: 'week' }, global: { stubs: { ConsoleLink: LinkStub } } });
    wrappers.push(wrapper);
    await flushPromises();

    expect(read).toHaveBeenCalledWith('week', expect.any(AbortSignal));
    expect(page).toHaveBeenCalledWith('week', { limit: 200 }, expect.any(AbortSignal));
    const head = wrapper.get('[aria-label="Entity"]');
    expect(head.text()).toContain('Tashkent Fashion Week');
    expect(head.text()).toContain('Ташкентская неделя моды · Toshkent moda haftaligi');
    expect(head.text()).toContain('Every year');
    expect(head.text()).toContain('86% sure');
    expect(head.text()).toContain('21 Nov 2027 · predicted');
    expect(head.text()).toContain('3 from 2 sources');
    expect(wrapper.findAll('section[aria-label]').map((section) => section.attributes('aria-label'))).toEqual([
      'Entity',
      '2027',
      '2026',
      '2025',
    ]);
    const year2025 = wrapper.get('section[aria-label="2025"]');
    expect(year2025.get('[aria-label="Occurrences"]').text()).toContain('held');
    expect(year2025.get('[aria-label="Occurrences"]').text()).toContain('Hilton, Tashkent, UZ');
    expect(year2025.get('[aria-label="Posts"]').text()).toContain('report');
    const update = wrapper.get('section[aria-label="2026"] [aria-label="Posts"]');
    expect(update.text()).toContain('update: place');
    expect(update.text()).toContain('Afisha Telegram');
    expect(JSON.parse(update.get('a[data-to]').attributes('data-to')!)).toEqual({
      kind: 'source',
      id: 'telegram',
      tab: 'posts',
      post: 'p1',
    });
  });

  it('says an entity another workspace holds is not found', async () => {
    session = {
      containerId: ref('pool'),
      readEntity: vi.fn(async () =>
        ResultExtensions.fail(ApiFailureFactory.create('http', new Response(null, { status: 404 }))),
      ),
      readEntityPosts: vi.fn(async () => ResultExtensions.ok({ data: [], next: '', hasMore: false })),
    } as unknown as HarvestConsole;
    const wrapper = mount(EntityPanel, { props: { entityId: 'gone' }, global: { stubs: { ConsoleLink: LinkStub } } });
    wrappers.push(wrapper);
    await flushPromises();
    expect(wrapper.text()).toContain('Entity not found');
  });
});
