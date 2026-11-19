/** Mirrors `EntityTypes`. */
export type EntityType = 'event' | 'listing';

/** Mirrors `EntityRoles`: what a post does for its entity. */
export type EntityRole = 'announcement' | 'update' | 'report' | 'mention' | 'unsure';

/** Mirrors `OccurrencePlaceModel`. */
export interface OccurrencePlaceModel {
  readonly venue?: string;
  readonly city?: string;
  readonly country?: string;
}

/** Mirrors `OccurrenceModel`: one run of an event. */
export interface OccurrenceModel {
  readonly id: string;
  /** The edition label a source gave, else the year it starts. */
  readonly label: string;
  /** `yyyy-MM-dd`; the first day of the month or year at a coarser precision. */
  readonly starts: string;
  readonly ends?: string;
  /** `year`, `month`, `day` or finer. */
  readonly precision: string;
  /** `stated` or `estimated`. */
  readonly certainty: string;
  /** The cadence predicted it and no source has announced it yet. */
  readonly predicted?: boolean;
  /** `scheduled`, `cancelled`, `postponed`, `held` or `unknown`. */
  readonly status: string;
  readonly place?: OccurrencePlaceModel;
}

/** Mirrors `EntityYearSummaryModel`. */
export interface EntityYearSummaryModel {
  readonly year: number;
  readonly occurrences: number;
  readonly predicted?: boolean;
  readonly posts: number;
}

/** Mirrors `EntitySummaryModel`: one entity a search finds. */
export interface EntitySummaryModel {
  readonly id: string;
  readonly containerId: string;
  readonly entityType: EntityType;
  readonly name: string;
  readonly aliases: readonly string[];
  readonly primaryDomain?: string;
  readonly status: string;
  /** `annual`, `semiannual`, `quarterly`, `monthly`, `irregular` or `unknown`. */
  readonly cadence: string;
  readonly score?: number;
  readonly years: readonly EntityYearSummaryModel[];
  /** `yyyy-MM-dd`, when the cadence predicts a next start. */
  readonly predicted?: string;
  readonly occurrences: number;
  readonly posts: number;
  readonly sources: number;
  readonly lastSeenAtUtc: string;
}

/** Mirrors `EntityTimelineYearModel`. */
export interface EntityTimelineYearModel {
  readonly year: number;
  readonly occurrences: readonly OccurrenceModel[];
  readonly posts: number;
}

/** Mirrors `EntityTimelineModel`: an entity with its occurrences by year. */
export interface EntityTimelineModel {
  readonly id: string;
  /** The entity asked for when it was merged into this one. */
  readonly requestedId?: string;
  readonly containerId: string;
  readonly entityType: EntityType;
  readonly name: string;
  readonly aliases: readonly string[];
  readonly primaryDomain?: string;
  readonly status: string;
  readonly cadence: string;
  readonly cadenceConfidence: number;
  readonly firstSeenAtUtc: string;
  readonly lastSeenAtUtc: string;
  readonly posts: number;
  readonly sources: number;
  readonly years: readonly EntityTimelineYearModel[];
}

/** Mirrors `EntityPostModel`: one post bound to an entity; its title and link, never its text. */
export interface EntityPostModel {
  readonly postId: string;
  readonly role: EntityRole;
  readonly changes: readonly string[];
  readonly occurrenceId?: string;
  readonly year: number;
  readonly title?: string;
  readonly link?: string;
  readonly sourceId?: string;
  readonly source?: string;
  readonly postedAt?: string;
  readonly firstSeenAtUtc: string;
  readonly boundAtUtc: string;
}

/** Mirrors `EntityCalendarEntryModel`: one occurrence on a calendar with its event. */
export interface EntityCalendarEntryModel {
  readonly entityId: string;
  readonly entityType: EntityType;
  readonly name: string;
  readonly primaryDomain?: string;
  readonly cadence: string;
  readonly occurrence: OccurrenceModel;
  readonly posts: number;
}

/** Mirrors `EntityEvent`: one change of the entity change feed. */
export interface EntityEventModel {
  readonly eventId: string;
  /** `entity.created`, `occurrence.added` and the other `EntityEventTypes`. */
  readonly type: string;
  readonly occurredAtUtc: string;
  readonly containerId: string;
  readonly entityId: string;
  readonly entityType: EntityType;
  readonly name: string;
  readonly primaryDomain?: string;
  readonly status: string;
  readonly cadence: string;
  readonly occurrenceId?: string;
  readonly occurrence?: OccurrenceModel;
  readonly previous?: OccurrenceModel;
  readonly changes: readonly string[];
  readonly relatedEntityId?: string;
  readonly replacesOccurrenceId?: string;
  readonly postId?: string;
}

/** The filters of an entity search. */
export interface EntitySearchQuery {
  /** The name to search for; blank lists by filters only, newest first. */
  readonly q?: string;
  readonly type?: EntityType;
  /** The taxonomy key the primary domain equals or sits under. */
  readonly domain?: string;
  /** 1 to 100; the server's default is 20. */
  readonly limit?: number;
}

/** The filters and page of an entity's posts. */
export interface EntityPostsQuery {
  readonly year?: number;
  readonly role?: EntityRole;
  /** 1 to 200; the server's default is 50. */
  readonly limit?: number;
  readonly cursor?: string;
}

/** The window of an entity calendar, at most 366 days. */
export interface EntityCalendarQuery {
  /** `yyyy-MM-dd`. */
  readonly from: string;
  /** `yyyy-MM-dd`. */
  readonly to: string;
  readonly domain?: string;
  readonly type?: EntityType;
}

/** The filters and page of the entity change feed. */
export interface EntityEventsQuery {
  readonly cursor?: string;
  readonly limit?: number;
  readonly type?: EntityType;
  readonly domain?: string;
}

/** Mirrors `EntityRouteConstants`: the routes, under `api/entities`, and their limits. */
export const EntityRouteConstants = {
  entities: 'api/entities',
  posts: 'posts',
  calendar: 'calendar',
  events: 'events',
  defaultSearchLimit: 20,
  maxSearchLimit: 100,
  defaultPostsLimit: 50,
  maxPostsLimit: 200,
  defaultEventsLimit: 100,
  maxEventsLimit: 500,
  maxCalendarDays: 366,
} as const;
