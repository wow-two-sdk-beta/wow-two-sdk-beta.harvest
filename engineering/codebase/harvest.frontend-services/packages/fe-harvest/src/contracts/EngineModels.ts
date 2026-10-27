import type { WorkKind } from './RegistrationEnums';

/** Mirrors a durable execution and its progress counters. */
export interface RunModel {
  readonly id: string;
  readonly containerId: string;
  readonly pipelineId: string;
  readonly kind: WorkKind;
  readonly status: 'queued' | 'running' | 'completed' | 'partial' | 'failed' | 'cancelled' | 'interrupted' | 'paused';
  readonly queuedAtUtc: string;
  readonly startedAtUtc?: string;
  readonly finishedAtUtc?: string;
  readonly captured: number;
  readonly unchanged: number;
  readonly discovered: number;
  readonly failed: number;
  readonly pages: number;
  readonly requests: number;
  readonly bytes: number;
  readonly fetchOutcome?: string;
  readonly completionReason?: string;
  readonly message?: string;
}

/** Mirrors the queue and refusal state of one source host lane. */
export interface HostHealthModel {
  readonly hostKey: string;
  readonly lane: string;
  readonly pausedUntilUtc?: string;
  readonly consecutiveFailures: number;
  readonly lastChallengeKind?: string;
  readonly queued: number;
  readonly running: number;
}

/** Mirrors one source view of a canonical post. */
export interface PostLinkModel {
  readonly id: string;
  readonly sourceId: string;
  readonly externalKey: string;
  readonly url: string;
  readonly state: string;
  readonly role: string;
  readonly captureId?: string;
  readonly capturedAtUtc?: string;
  readonly lastCheckedAtUtc?: string;
  readonly lastSeenAtUtc?: string;
  readonly nextCheckEligibleAtUtc?: string;
  readonly lastCheckOutcome?: string;
  readonly lastCheckHttpStatus?: number;
  readonly lastCheckRunId?: string;
  readonly lastCheckRunKind?: string;
  readonly checkCount?: number;
  readonly sourceFields?: Readonly<Record<string, unknown>>;
}

/** Mirrors a canonical post, its source links and field provenance. */
export interface PostModel {
  readonly id: string;
  readonly containerId: string;
  readonly state: 'live' | 'suspect' | 'gone';
  readonly revision: number;
  readonly base: {
    readonly schema: string;
    readonly kind?: string;
    readonly title?: string;
    readonly text?: string;
    readonly author?: { readonly name?: string; readonly url?: string; readonly id?: string };
    /** Published strings are displayed directly; this view performs no decimal arithmetic. */
    readonly prices?: readonly {
      readonly raw?: string;
      readonly currency?: string;
      readonly role: string;
      readonly free: boolean;
    }[];
    readonly postedAt?: { readonly value: string; readonly precision: string; readonly timeZone?: string };
    readonly dates?: readonly {
      readonly role: string;
      readonly value: string;
      readonly end?: string;
      readonly precision: string;
      readonly raw?: string;
    }[];
    readonly location?: {
      readonly mentions: readonly { readonly text: string; readonly kind: string }[];
      readonly geo?: { readonly latitude: number; readonly longitude: number };
    };
    readonly contacts?: readonly { readonly kind: string; readonly value: string; readonly raw?: string }[];
    readonly media?: readonly { readonly kind: string; readonly url: string; readonly role?: string }[];
    readonly links?: readonly { readonly kind: string; readonly url: string; readonly text?: string }[];
    readonly sourceTags?: readonly string[];
    readonly tags?: readonly { readonly tag: string; readonly tier: string; readonly version?: string }[];
    readonly flags?: readonly string[];
    readonly extraction?: {
      readonly normVersion: string;
      readonly rulesVersion: string;
      readonly ai?: unknown;
    };
    readonly language?: {
      readonly code: string;
      readonly script?: string;
      readonly mixed: boolean;
      readonly confidence?: number;
    };
    readonly extras?: Readonly<Record<string, unknown>>;
  };
  readonly links: readonly PostLinkModel[];
  readonly fieldSources: Readonly<Record<string, string>>;
  readonly firstSeenAtUtc: string;
  readonly lastChangedAtUtc: string;
  readonly mergedInto?: string;
}

/** Mirrors a tenant's pipeline subscription. */
export interface SubscriptionModel {
  readonly id: string;
  readonly tenantId: string;
  readonly pipelineId?: string;
  readonly schema?: string;
  readonly filterKey?: string;
  readonly mode: string;
  readonly state: string;
  readonly context: Readonly<Record<string, string>>;
  readonly createdAtUtc: string;
}

/** Mirrors a pull delivery event and its immutable payload snapshot. */
export interface DeliveryEvent {
  readonly eventId: string;
  readonly subscriptionId: string;
  readonly type: string;
  readonly postId?: string;
  readonly revision?: number;
  readonly occurredAtUtc: string;
  readonly data?: unknown;
}

/** Carries an opaque, resumable subscription cursor. */
export interface CursorPage<T> {
  readonly data: readonly T[];
  readonly next: string;
  readonly hasMore: boolean;
}
