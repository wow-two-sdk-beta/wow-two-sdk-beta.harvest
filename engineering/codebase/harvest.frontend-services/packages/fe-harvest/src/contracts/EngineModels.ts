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
