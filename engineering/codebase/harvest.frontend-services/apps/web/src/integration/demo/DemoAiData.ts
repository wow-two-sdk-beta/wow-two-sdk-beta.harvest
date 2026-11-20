import type {
  EntityCalendarEntryModel,
  EntityPostModel,
  EntitySummaryModel,
  EntityTimelineModel,
  EntityType,
  OccurrenceModel,
  ProcessedAnswerModel,
  ProcessedEvidenceModel,
  ProcessedExtractionModel,
  ProcessedPassModel,
  ProcessedTagsModel,
  SourceProcessedPage,
  SourceProcessedPostDetailsModel,
  SourceProcessedPostModel,
} from '@wow-two-beta/fe-harvest/contracts';

/** A post of the demo's Afisha.uz, as its Posts list shows it. */
export interface DemoPost {
  readonly postId: string;
  readonly title?: string;
  readonly kind: string;
  readonly state?: string;
  readonly revision: number;
  readonly firstSeenAt: string;
  readonly postedAt?: string;
  readonly language?: string;
  readonly pipelineId?: string;
  readonly url?: string;
  readonly imageUrl?: string;
}

/** A local day `offset` days from today, as `yyyy-MM-dd`. @internal */
function day(offset: number, years = 0) {
  const date = new Date();
  date.setDate(date.getDate() + offset);
  date.setFullYear(date.getFullYear() + years);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** An answer's provenance: who answered, the cost, latency and tokens the inference log recorded. @internal */
function answer(
  model: string,
  costMicroUsd: number,
  latencyMs: number,
  promptVersion: number,
  at: string,
  tokens?: readonly [input: number, output: number],
): ProcessedAnswerModel {
  const [provider, id] = model.split('/');
  return {
    provider,
    model: id,
    promptVersion,
    costMicroUsd,
    latencyMs,
    ...(tokens ? { inputTokens: tokens[0], outputTokens: tokens[1] } : {}),
    answeredAt: at,
  };
}

const Haiku = 'anthropic/claude-haiku-5-5';
const Mini = 'openai/gpt-5-mini';
const Flash = 'google/gemini-2.5-flash';

/** The AI's reading of each demo post, by title; a post missing here is not processed yet. @internal */
function readingOf(
  post: DemoPost,
): Pick<SourceProcessedPostModel, 'tags' | 'classification' | 'extraction'> | undefined {
  const at = post.firstSeenAt;
  const tags = (value: Partial<ProcessedTagsModel> & Pick<ProcessedTagsModel, 'answer'>): ProcessedTagsModel => ({
    status: 'done',
    current: true,
    domains: [],
    topics: [],
    flags: [],
    ...value,
  });
  const fashion = { key: 'style-fashion', label: 'Style & Fashion', primary: false };
  switch (post.title) {
    case 'Показ мод The Fashion Show Line 2026':
      return {
        tags: tags({
          entityType: 'event',
          domains: [{ key: 'style-fashion/womens-fashion', label: "Women's Fashion", primary: true }, fashion],
          topics: ['fashion-show', 'runway', 'autumn-collections'],
          about: 'a runway show of Tashkent designers’ autumn collections',
          timeframe: 'upcoming',
          titleEn: 'Fashion show: The Fashion Show Line 2026',
          confidence: 0.94,
          answer: answer(Haiku, 310, 1840, 2, at, [1240, 186]),
        }),
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
            originalName: 'The Fashion Show Line',
            editionLabel: '2026',
            occurrences: [{ starts: '2026-11-21T12:00', precision: 'minute', certainty: 'stated' }],
            venue: 'Youth Creativity Palace',
            city: 'Tashkent',
            country: 'UZ',
            format: 'runway',
            access: 'ticketed',
            prices: [{ amount: 150000, currency: 'UZS', label: 'entry' }],
            status: 'scheduled',
          },
          flags: [],
          answer: answer(Haiku, 1120, 2410, 3, at, [2380, 412]),
        },
      };
    case 'Autumn collections at a Tashkent showroom':
      return {
        tags: tags({
          entityType: 'event',
          domains: [{ key: 'style-fashion/designer-clothing', label: 'Designer Clothing', primary: true }, fashion],
          topics: ['showroom', 'autumn-collections'],
          about: 'a showroom presents autumn collections',
          timeframe: 'upcoming',
          confidence: 0.71,
          flags: [{ field: 'timeframe', reason: 'the post states no date', proposed: 'upcoming' }],
          answer: answer(Mini, 540, 3120, 2, at, [1190, 224]),
        }),
        classification: {
          classifier: 'model:openai/gpt-5-mini@post-tags:2',
          verdict: 'relevant',
          confidence: 0.71,
          reason: 'event under style-fashion',
          kind: 'exhibition',
        },
        extraction: {
          useCase: 'extract-event',
          status: 'done',
          role: 'mention',
          event: {
            name: 'Autumn collections showroom',
            occurrences: [{ starts: day(10).slice(0, 7), precision: 'month', certainty: 'estimated' }],
            city: 'Tashkent',
            format: 'exhibition',
            access: 'free',
            prices: [],
            status: 'scheduled',
          },
          flags: [{ field: 'occurrences[0].starts', reason: 'estimated from the publication date', proposed: day(10) }],
          answer: answer(Mini, 1680, 4010, 3, at, [2410, 468]),
        },
      };
    case 'Five local designers to follow':
    case 'A guide to the season’s fabrics':
      return {
        tags: tags({
          entityType: 'none',
          domains: [fashion],
          topics: post.title.startsWith('Five') ? ['designers', 'local-brands'] : ['fabrics', 'textiles'],
          about: post.title.startsWith('Five') ? 'profiles of local fashion designers' : 'a guide to seasonal fabrics',
          timeframe: 'evergreen',
          confidence: 0.88,
          answer: answer(Haiku, 270, 1290, 2, at, [1020, 142]),
        }),
        classification: {
          classifier: 'model:anthropic/claude-haiku-5-5@post-tags:2',
          verdict: 'irrelevant',
          confidence: 0.88,
          reason: 'entity type none',
        },
      };
    case 'Ceramics of Rishtan: a retrospective':
      return {
        tags: tags({
          entityType: 'event',
          domains: [{ key: 'hobbies-interests/arts-and-crafts', label: 'Arts and Crafts', primary: true }],
          topics: ['ceramics', 'rishtan', 'retrospective'],
          about: 'a retrospective of Rishtan ceramics',
          timeframe: 'ongoing',
          titleEn: 'Ceramics of Rishtan: a retrospective',
          confidence: 0.9,
          answer: answer(Flash, 180, 1380, 2, at, [1110, 171]),
        }),
        classification: {
          classifier: 'model:google/gemini-2.5-flash@post-tags:2',
          verdict: 'irrelevant',
          confidence: 0.9,
          reason: 'off every wanted pair',
        },
      };
    case 'Demo: Exhibition across two years':
      return {
        tags: tags({
          entityType: 'unsure',
          domains: [],
          topics: ['exhibition'],
          about: 'an exhibition across two years',
          timeframe: 'unsure',
          confidence: 0.42,
          flags: [
            { field: 'entityType', reason: 'unsure', proposed: 'event' },
            { field: 'domains', reason: 'unknown node', proposed: 'fine-art/installations' },
          ],
          answer: answer(Haiku, 290, 1730, 2, at, [1080, 158]),
        }),
        classification: {
          classifier: 'model:anthropic/claude-haiku-5-5@post-tags:2',
          verdict: 'uncertain',
          confidence: 0.42,
          reason: 'entity type unsure',
        },
      };
    case 'Demo: One-day exhibition last year':
      return {
        tags: tags({ status: 'failed', error: 'rate-limited', answer: answer(Haiku, 0, 30000, 2, at) }),
      };
    case 'Inside the new photography hall':
    case 'What to see at the museum this month':
      return {
        tags: tags({
          entityType: 'none',
          domains: [{ key: 'fine-art/photography', label: 'Photography', primary: true }],
          topics: ['museum', 'photography'],
          about: 'a guide to museum exhibitions',
          timeframe: 'ongoing',
          confidence: 0.83,
          answer: answer(Haiku, 250, 1180, 2, at, [960, 131]),
        }),
        classification: {
          classifier: 'model:anthropic/claude-haiku-5-5@post-tags:2',
          verdict: 'irrelevant',
          confidence: 0.83,
          reason: 'entity type none',
        },
      };
    default:
      return undefined;
  }
}

/** How many of the demo posts carry a tags answer, as the source overview's `processedCount` reads it. */
export function demoProcessedCount(posts: readonly DemoPost[]): number {
  return posts.filter((post) => readingOf(post) !== undefined).length;
}

/** The demo posts the AI passes read, each with its post fields and what the passes made of it. @internal */
function processedPostsOf(posts: readonly DemoPost[]): SourceProcessedPostModel[] {
  return posts.flatMap((post): SourceProcessedPostModel[] => {
    const reading = readingOf(post);
    return reading
      ? [
          {
            postId: post.postId,
            title: post.title ?? post.url ?? 'Untitled',
            url: post.url,
            revision: post.revision,
            firstSeenAt: post.firstSeenAt,
            kind: post.kind,
            state: post.state ?? 'live',
            ...(post.postedAt ? { postedAt: post.postedAt } : {}),
            ...(post.language ? { language: post.language } : {}),
            ...(post.pipelineId ? { pipelineId: post.pipelineId } : {}),
            ...(post.imageUrl ? { imageUrl: post.imageUrl } : {}),
            ...reading,
          },
        ]
      : [];
  });
}

/** Answers `GET api/sources/{id}/processed` over the demo posts: filters, then a cursor page of the given size. */
export function demoProcessedPage(posts: readonly DemoPost[], params: URLSearchParams): SourceProcessedPage {
  const items = processedPostsOf(posts);
  const type = params.get('entityType');
  const verdict = params.get('verdict');
  const extracted = params.get('extracted');
  const hasExtraction = (post: SourceProcessedPostModel) =>
    (post.extraction as ProcessedExtractionModel | undefined)?.status === 'done';
  const matches = items.filter(
    (post) =>
      (!type || post.tags.entityType === type) &&
      (!verdict || post.classification?.verdict === verdict) &&
      (!extracted || hasExtraction(post) === (extracted === 'true')),
  );
  const offset = Number(params.get('cursor') ?? 0);
  const limit = Number(params.get('limit') ?? 30);
  const end = offset + limit;
  return {
    items: matches.slice(offset, end),
    ...(end < matches.length ? { nextCursor: String(end) } : {}),
    total: matches.length,
  };
}

/** The quotes the demo's extraction answers keep behind their fields, by post title. @internal */
function evidenceOf(post: SourceProcessedPostModel): ProcessedEvidenceModel[] {
  switch (post.title) {
    case 'Показ мод The Fashion Show Line 2026':
      return [
        {
          field: 'occurrences[0]',
          certainty: 'stated',
          quotes: ['Показ состоится 21 ноября в 12:00 во Дворце творчества молодёжи'],
        },
        { field: 'prices[0]', quotes: ['Билеты на показ — от 150 000 сум'] },
        {
          field: 'registration.deadline',
          value: day(5),
          quotes: ['Регистрация гостей открыта до конца этой недели'],
        },
      ];
    case 'Autumn collections at a Tashkent showroom':
      return [
        {
          field: 'occurrences[0]',
          certainty: 'estimated',
          quotes: ['the autumn collections go on show in a few weeks'],
        },
      ];
    default:
      return [];
  }
}

/** A stable inference id for a demo answer, so the page's run column reads like the host's. @internal */
function runIdOf(postId: string, pass: number) {
  return `${postId.replace(/\W/g, '').slice(-8).padStart(8, '0')}-0000-4000-8000-${String(pass).padStart(12, '0')}`;
}

/** Answers `GET api/sources/{id}/processed/{postId}`: the post, the quotes behind its extraction and each pass. */
export function demoProcessedPost(
  posts: readonly DemoPost[],
  postId: string,
): SourceProcessedPostDetailsModel | undefined {
  const post = processedPostsOf(posts).find((item) => item.postId === postId);
  if (!post) return undefined;
  const passes: ProcessedPassModel[] = [
    {
      pass: 'post-tags',
      status: post.tags.status,
      ...(post.tags.error ? { error: post.tags.error } : {}),
      inferenceId: runIdOf(post.postId, 1),
      answer: post.tags.answer,
    },
    ...(post.extraction
      ? [
          {
            pass: post.extraction.useCase,
            status: post.extraction.status,
            ...(post.extraction.error ? { error: post.extraction.error } : {}),
            inferenceId: runIdOf(post.postId, 2),
            answer: post.extraction.answer,
          },
        ]
      : []),
  ];
  return { post, evidence: post.extraction?.status === 'done' ? evidenceOf(post) : [], passes };
}

/** One synthetic entity with its occurrences and posts, the years and counts derived. @internal */
interface DemoEntity {
  readonly id: string;
  readonly entityType: EntityType;
  readonly name: string;
  readonly aliases: readonly string[];
  readonly primaryDomain: string;
  readonly cadence: string;
  readonly cadenceConfidence: number;
  readonly occurrences: readonly OccurrenceModel[];
  readonly posts: readonly EntityPostModel[];
}

/** The demo's entities, dated around today so the calendar always has a next run to show. */
export function demoEntities(afishaId: string, posts: readonly DemoPost[], fixed: (n: number) => string) {
  const telegram = fixed(201);
  const uzexpo = fixed(205);
  const byTitle = (title: string) => posts.find((post) => post.title === title);
  const occurrence = (
    n: number,
    starts: string,
    status: string,
    more: Partial<OccurrenceModel> = {},
  ): OccurrenceModel => ({
    id: fixed(n),
    label: starts.slice(0, 4),
    starts,
    precision: 'day',
    certainty: 'stated',
    status,
    ...more,
  });
  const post = (
    n: number,
    role: EntityPostModel['role'],
    year: number,
    title: string,
    sourceId: string,
    source: string,
    postedAt: string,
    more: Partial<EntityPostModel> = {},
  ): EntityPostModel => ({
    postId: fixed(n),
    role,
    changes: [],
    year,
    title,
    sourceId,
    source,
    postedAt,
    firstSeenAtUtc: `${postedAt}T09:00:00Z`,
    boundAtUtc: `${postedAt}T09:05:00Z`,
    ...more,
  });
  const year = (offset: number) => Number(day(0, offset).slice(0, 4));
  const show = byTitle('Показ мод The Fashion Show Line 2026');
  const showroom = byTitle('Autumn collections at a Tashkent showroom');
  const ceramics = byTitle('Ceramics of Rishtan: a retrospective');
  const thisRun = day(2);
  const entities: DemoEntity[] = [
    {
      id: fixed(900),
      entityType: 'event',
      name: 'Tashkent Fashion Week',
      aliases: ['Ташкентская неделя моды', 'Toshkent moda haftaligi'],
      primaryDomain: 'style-fashion/womens-fashion',
      cadence: 'annual',
      cadenceConfidence: 0.86,
      occurrences: [
        occurrence(910, day(2, -3), 'held', {
          ends: day(4, -3),
          place: { venue: 'Hilton', city: 'Tashkent', country: 'UZ' },
        }),
        occurrence(911, day(-1, -2), 'held', {
          ends: day(1, -2),
          place: { venue: 'Hilton', city: 'Tashkent', country: 'UZ' },
        }),
        occurrence(912, day(5, -1), 'held', {
          ends: day(7, -1),
          place: { venue: 'Youth Creativity Palace', city: 'Tashkent', country: 'UZ' },
        }),
        occurrence(913, thisRun, 'scheduled', {
          ends: day(4),
          place: { venue: 'Youth Creativity Palace', city: 'Tashkent', country: 'UZ' },
        }),
        occurrence(914, day(2, 1), 'scheduled', { certainty: 'estimated', predicted: true }),
      ],
      posts: [
        ...(show
          ? [
              post(0, 'announcement', year(0), show.title!, afishaId, 'Afisha.uz', day(-36), {
                postId: show.postId,
                occurrenceId: fixed(913),
              }),
            ]
          : []),
        ...(showroom
          ? [
              post(0, 'mention', year(0), showroom.title!, afishaId, 'Afisha.uz', day(-2), {
                postId: showroom.postId,
                occurrenceId: fixed(913),
              }),
            ]
          : []),
        post(
          920,
          'update',
          year(0),
          'Неделя моды переносится в Дворец творчества',
          telegram,
          'Afisha Telegram',
          day(-12),
          {
            changes: ['place'],
            occurrenceId: fixed(913),
          },
        ),
        post(921, 'report', year(-1), 'Итоги Tashkent Fashion Week', telegram, 'Afisha Telegram', day(9, -1), {
          occurrenceId: fixed(912),
        }),
        post(922, 'announcement', year(-1), 'Tashkent Fashion Week returns', afishaId, 'Afisha.uz', day(-40, -1), {
          occurrenceId: fixed(912),
        }),
        post(923, 'announcement', year(-2), 'Moda haftaligi boshlanmoqda', telegram, 'Afisha Telegram', day(-30, -2), {
          occurrenceId: fixed(911),
        }),
        post(924, 'report', year(-3), 'Fashion week: the collections', afishaId, 'Afisha.uz', day(6, -3), {
          occurrenceId: fixed(910),
        }),
      ],
    },
    {
      id: fixed(901),
      entityType: 'event',
      name: 'Uzbekistan Textile Expo',
      aliases: ['Textile Expo Uzbekistan', 'Текстиль Экспо'],
      primaryDomain: 'style-fashion/textiles',
      cadence: 'annual',
      cadenceConfidence: 0.74,
      occurrences: [
        occurrence(930, day(-310), 'held', {
          ends: day(-308),
          place: { venue: 'Uzexpocentre', city: 'Tashkent', country: 'UZ' },
        }),
        occurrence(931, day(-310, -1), 'held', { place: { venue: 'Uzexpocentre', city: 'Tashkent', country: 'UZ' } }),
        occurrence(932, day(55), 'scheduled', { certainty: 'estimated', predicted: true }),
      ],
      posts: [
        post(
          940,
          'announcement',
          Number(day(-310).slice(0, 4)),
          'Textile Expo: exhibitor registration open',
          uzexpo,
          'uzexpocentre.uz',
          day(-340),
          {
            occurrenceId: fixed(930),
          },
        ),
        post(
          941,
          'report',
          Number(day(-305).slice(0, 4)),
          'Textile Expo closes with 240 exhibitors',
          uzexpo,
          'uzexpocentre.uz',
          day(-305),
          {
            occurrenceId: fixed(930),
          },
        ),
        post(
          942,
          'report',
          Number(day(-305, -1).slice(0, 4)),
          'Textile Expo: the first edition in review',
          uzexpo,
          'uzexpocentre.uz',
          day(-305, -1),
          {
            occurrenceId: fixed(931),
          },
        ),
      ],
    },
    {
      id: fixed(902),
      entityType: 'event',
      name: 'Rishtan Ceramics Festival',
      aliases: ['Риштанский фестиваль керамики'],
      primaryDomain: 'hobbies-interests/arts-and-crafts',
      cadence: 'irregular',
      cadenceConfidence: 0,
      occurrences: [
        occurrence(950, day(-200, -1), 'held', { place: { city: 'Rishtan', country: 'UZ' } }),
        occurrence(951, day(120), 'scheduled', { ends: day(123), place: { city: 'Rishtan', country: 'UZ' } }),
      ],
      posts: [
        ...(ceramics
          ? [post(0, 'mention', year(0), ceramics.title!, afishaId, 'Afisha.uz', day(-3), { postId: ceramics.postId })]
          : []),
        post(
          960,
          'announcement',
          year(0),
          'Rishtan ceramics festival dates announced',
          telegram,
          'Afisha Telegram',
          day(-15),
          {
            occurrenceId: fixed(951),
          },
        ),
      ],
    },
    {
      id: fixed(903),
      entityType: 'listing',
      name: '2-room flat, Chilonzor 9',
      aliases: ['2 xonali kvartira, Chilonzor 9-kvartal'],
      primaryDomain: 'real-estate/apartments',
      cadence: 'unknown',
      cadenceConfidence: 0,
      occurrences: [],
      posts: [
        post(970, 'announcement', year(0), '2 xonali kvartira ijaraga, Chilonzor', fixed(211), 'olx.uz', day(-9)),
        post(971, 'update', year(0), '2 xonali kvartira ijaraga — narx tushdi', fixed(211), 'olx.uz', day(-4), {
          changes: ['price'],
        }),
      ],
    },
  ];

  const yearOf = (post: EntityPostModel) => post.year;
  function summary(entity: DemoEntity, score?: number): EntitySummaryModel {
    const years = [
      ...new Set([...entity.occurrences.map((row) => Number(row.starts.slice(0, 4))), ...entity.posts.map(yearOf)]),
    ].sort();
    const predicted = entity.occurrences.find((row) => row.predicted);
    return {
      id: entity.id,
      containerId: '',
      entityType: entity.entityType,
      name: entity.name,
      aliases: entity.aliases,
      primaryDomain: entity.primaryDomain,
      status: 'active',
      cadence: entity.cadence,
      ...(score !== undefined ? { score } : {}),
      years: years.map((value) => ({
        year: value,
        occurrences: entity.occurrences.filter((row) => !row.predicted && row.starts.startsWith(String(value))).length,
        predicted: entity.occurrences.some((row) => row.predicted && row.starts.startsWith(String(value))),
        posts: entity.posts.filter((row) => row.year === value).length,
      })),
      ...(predicted ? { predicted: predicted.starts } : {}),
      occurrences: entity.occurrences.filter((row) => !row.predicted).length,
      posts: entity.posts.length,
      sources: new Set(entity.posts.map((row) => row.sourceId)).size,
      lastSeenAtUtc:
        entity.posts
          .map((row) => row.boundAtUtc)
          .sort()
          .at(-1) ?? new Date().toISOString(),
    };
  }

  return {
    search(params: URLSearchParams): EntitySummaryModel[] {
      const q = params.get('q')?.trim().toLowerCase();
      const type = params.get('type');
      const domain = params.get('domain');
      return entities
        .filter((entity) => !type || entity.entityType === type)
        .filter((entity) => !domain || entity.primaryDomain === domain || entity.primaryDomain.startsWith(`${domain}/`))
        .flatMap((entity) => {
          if (!q) return [summary(entity)];
          const hit = [entity.name, ...entity.aliases].some((name) => name.toLowerCase().includes(q));
          return hit ? [summary(entity, 0.82)] : [];
        });
    },
    get(entityId: string): EntityTimelineModel | undefined {
      const entity = entities.find((row) => row.id === entityId);
      if (!entity) return undefined;
      const head = summary(entity);
      return {
        id: entity.id,
        containerId: '',
        entityType: entity.entityType,
        name: entity.name,
        aliases: entity.aliases,
        primaryDomain: entity.primaryDomain,
        status: 'active',
        cadence: entity.cadence,
        cadenceConfidence: entity.cadenceConfidence,
        firstSeenAtUtc: entity.posts.map((row) => row.boundAtUtc).sort()[0] ?? head.lastSeenAtUtc,
        lastSeenAtUtc: head.lastSeenAtUtc,
        posts: head.posts,
        sources: head.sources,
        years: head.years.map((row) => ({
          year: row.year,
          occurrences: entity.occurrences.filter((item) => item.starts.startsWith(String(row.year))),
          posts: row.posts,
        })),
      };
    },
    posts(entityId: string, params: URLSearchParams) {
      const entity = entities.find((row) => row.id === entityId);
      if (!entity) return undefined;
      const year = params.get('year');
      const rows = [...entity.posts]
        .filter((row) => !year || String(row.year) === year)
        .sort((a, b) => (b.postedAt ?? '').localeCompare(a.postedAt ?? ''));
      const offset = Number(params.get('cursor') || 0);
      const limit = Number(params.get('limit') ?? 50);
      return {
        data: rows.slice(offset, offset + limit),
        next: String(offset + limit),
        hasMore: offset + limit < rows.length,
      };
    },
    calendar(params: URLSearchParams): EntityCalendarEntryModel[] {
      const from = params.get('from') ?? day(0);
      const to = params.get('to') ?? day(365);
      const type = params.get('type');
      return entities
        .filter((entity) => !type || entity.entityType === type)
        .flatMap((entity) =>
          entity.occurrences
            .filter((row) => (row.ends ?? row.starts) >= from && row.starts <= to)
            .map((row) => ({
              entityId: entity.id,
              entityType: entity.entityType,
              name: entity.name,
              primaryDomain: entity.primaryDomain,
              cadence: entity.cadence,
              occurrence: row,
              posts: entity.posts.filter((item) => item.occurrenceId === row.id).length,
            })),
        )
        .sort((a, b) => a.occurrence.starts.localeCompare(b.occurrence.starts));
    },
  };
}
