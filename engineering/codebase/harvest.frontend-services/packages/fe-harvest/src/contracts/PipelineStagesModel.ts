import type { RunModel } from './EngineModels';
import type { ScheduleModel } from './PipelineModel';

export type PipelineStageKind = 'discover' | 'harvest' | 'process';
export type DiscoveryState = 'pending' | 'captured' | 'failed' | 'gone';

export interface PipelineStageOptions {
  readonly batchSize: number;
  readonly disabled?: boolean;
  readonly startsOn?: string;
  readonly titleContains?: string;
  readonly publishedSinceUtc?: string;
  readonly processor?: string;
  readonly model?: string;
  /** The published prompt version selected for this processor. */
  readonly promptVersion?: number;
}

export interface PipelineStageModel {
  /** Why the selected processor cannot run yet; configuration remains editable. */
  readonly readinessError?: string | null;
  readonly kind: PipelineStageKind;
  readonly catalog?: { readonly label: string; readonly url: string; readonly key?: string };
  readonly available: boolean;
  readonly schedule?: ScheduleModel;
  readonly options: PipelineStageOptions;
  readonly pending?: number;
  readonly runs: readonly RunModel[];
  readonly operationKeys?: readonly string[];
  readonly processorKeys?: readonly string[];
  readonly processorLabels?: Readonly<Record<string, string>>;
}

export interface PipelineDiscoveryModel {
  readonly id: string;
  readonly externalKey: string;
  readonly url: string;
  readonly title?: string;
  readonly publishedAtUtc?: string;
  readonly lastSeenAtUtc: string;
  readonly catalogUrl: string;
  readonly state: DiscoveryState;
  readonly nextDetailAtUtc: string;
  readonly postId?: string;
  readonly nativeFields?: Readonly<Record<string, unknown>>;
}

export interface RunAttemptModel {
  readonly workItemId: string;
  readonly step: string;
  readonly target: string;
  readonly attempt: number;
  readonly startedAtUtc: string;
  readonly finishedAtUtc?: string;
  readonly outcome?: string;
  readonly message?: string;
  readonly retryAtUtc?: string;
  readonly httpStatus?: number;
  readonly discovered: number;
  readonly operations?: readonly RunOperationModel[];
}

/** A single recorded operation inside a page, listing, or saved-capture attempt. */
export interface RunOperationModel {
  readonly key: string;
  readonly startedAtUtc: string;
  readonly finishedAtUtc?: string;
  readonly result?: {
    readonly status: 'completed' | 'failed' | 'skipped' | 'interrupted';
    readonly input?: number;
    readonly output?: number;
    readonly message?: string;
    readonly httpStatus?: number;
  };
}
