import type { SourceFamily, WorkKind } from './RegistrationEnums';

/** How a source is doing, as the overview decides it: a run in flight, a host or harvest problem, harvesting, or idle. */
export type SourceOverviewState = 'harvesting' | 'healthy' | 'needsAttention' | 'idle';

/** The statuses a harvest summary reports. */
export type SourceHarvestStatus = 'completed' | 'partial' | 'failed' | 'cancelled' | 'running' | 'queued';

/** Mirrors the overview's newest capture run of a source or a section: when, how it ended and what it found. */
export interface SourceHarvestModel {
  readonly runId: string;
  readonly pipelineId: string;
  /** The run's kind; present on a section's last run. */
  readonly kind?: WorkKind;
  /** `finishedAt`, else `queuedAt` (UTC ISO-8601). */
  readonly at: string;
  readonly status: SourceHarvestStatus;
  /** The run's `RunReason`, snake_case as stored, such as `page_limit`. */
  readonly reason?: string;
  /** Posts first seen in the run (`run.captured`). */
  readonly newPosts: number;
  /** Links the run found (`run.discovered`). */
  readonly found: number;
}

/** Mirrors one section of a source that a pipeline follows. */
export interface SourceSectionOverviewModel {
  readonly pipelineId: string;
  readonly name: string;
  readonly url?: string;
  /** The pipeline is not paused and has an enabled capture schedule. */
  readonly harvesting: boolean;
  readonly paused: boolean;
  /** The enabled capture schedule's interval, ISO-8601 (`PT6H`); omitted when off. */
  readonly captureEvery?: string;
  /** The enabled check schedule's interval, ISO-8601 (`P1D`); omitted when off. */
  readonly checkEvery?: string;
  readonly maxPages?: number;
  readonly lastRun?: SourceHarvestModel;
  readonly nextDueAt?: string;
  /** Posts whose first link capture came from this section's runs. */
  readonly posts: number;
  /** Active subscriptions on this section. */
  readonly subscribers: number;
}

/** Mirrors the newest refusal or challenge a source's host answered. */
export interface SourceRefusalModel {
  readonly at: string;
  readonly outcome: string;
  readonly message?: string;
}

/** Mirrors how politely a source is read and whether its host is held back. */
export interface SourceHealthModel {
  readonly hostKey: string;
  /** The gap between two page reads, in milliseconds. */
  readonly minDelayMs: number;
  readonly pausedUntil?: string;
  readonly pauseReason?: string;
  readonly consecutiveFailures: number;
  readonly lastRefusal?: SourceRefusalModel;
}

/** Mirrors `SourceOverviewModel`: one registered source as the console's Sources area shows it. */
export interface SourceOverviewModel {
  readonly sourceId: string;
  /** The display name, falling back to the handle. */
  readonly name: string;
  readonly family: SourceFamily;
  readonly handle: string;
  /** Omitted when the source belongs to no group. */
  readonly groupId?: string;
  readonly state: SourceOverviewState;
  /** The newest capture run across the source's sections; omitted when none ran. */
  readonly lastHarvest?: SourceHarvestModel;
  /** The earliest next capture among enabled schedules of unpaused sections. */
  readonly nextHarvestAt?: string;
  /** New posts per UTC day over 14 days, oldest first; the last entry is today. */
  readonly newPostsDaily: readonly number[];
  readonly sections: readonly SourceSectionOverviewModel[];
  readonly health: SourceHealthModel;
}

/** Mirrors `SourceGroupModel`: a publisher whose channels or branches are its sources. */
export interface SourceGroupModel {
  readonly id: string;
  readonly containerId: string;
  readonly name: string;
  readonly description?: string;
}

/** Declares a group, idempotent by name. */
export interface SourceGroupDraft {
  readonly containerId: string;
  readonly name: string;
  readonly description?: string;
}

/** Sets a source's display name and group; `clearGroup` with `groupId: null` ungroups it. */
export interface SourceUpdate {
  readonly name?: string;
  readonly groupId?: string | null;
  readonly clearGroup?: boolean;
}

/** Sets the gap between two page reads of a source's host, 1000 to 60000 ms. */
export interface SourceReadingUpdate {
  readonly minDelayMs: number;
}

/** Mirrors `SourcePostModel`: one post of a source, newest first in its list. */
export interface SourcePostModel {
  readonly postId: string;
  readonly title?: string;
  /** The `PostKind`, such as `event` or `article`. */
  readonly kind: string;
  readonly state: string;
  readonly revision: number;
  readonly firstSeenAt: string;
  readonly lastChangedAt: string;
  /** The section that first captured it. */
  readonly pipelineId?: string;
  /** The event start as stated. */
  readonly startsAt?: string;
  /** The event end as stated; omitted when the post states none. */
  readonly endsAt?: string;
  /** The link's URL. */
  readonly url?: string;
  /** The post's first image, as the source published it; omitted when it has none. */
  readonly imageUrl?: string;
}

/** Mirrors `PostRevisionModel`: one revision of a post and the top-level fields that changed. */
export interface PostRevisionModel {
  readonly revision: number;
  readonly at: string;
  /** `created` for the first revision; otherwise the changed `base` field names, such as `location`. */
  readonly changed: readonly string[];
}
