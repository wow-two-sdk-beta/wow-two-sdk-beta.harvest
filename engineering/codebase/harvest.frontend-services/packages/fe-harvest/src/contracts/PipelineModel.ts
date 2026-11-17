import type { AiMode, PipelinePauseReason, PipelineState, ScheduleMode, WorkKind } from './RegistrationEnums';
import type { PipelineStageOptions } from './PipelineStagesModel';

/** Mirrors `ScheduleModel`: one cadence row; durations travel as ISO 8601 (`PT1H`), times of day as `HH:mm:ss`. */
export interface ScheduleModel {
  readonly id: string;
  readonly kind: WorkKind;
  readonly mode: ScheduleMode;
  readonly every?: string;
  readonly maxEvery?: string;
  readonly cron?: string;
  readonly timeZone: string;
  readonly window?: { readonly start: string; readonly end: string };
  readonly enabled: boolean;
  readonly nextDueAtUtc?: string;
  readonly stage?: PipelineStageOptions;
}

/** Replaces the saved target and cadence; run history and source identity remain intact. */
export interface PipelineUpdate {
  readonly name?: string;
  readonly scope: PipelineModel['scope'];
  readonly schedules: readonly Omit<ScheduleModel, 'id' | 'nextDueAtUtc'>[];
  /** Only supplied values change; omitted values keep their saved limits. */
  readonly limits?: Pick<PipelineLimits, 'maxPages' | 'pageSize' | 'maxItems'>;
}

/** Stored run bounds; adapters may impose narrower ranges through their scope schema. */
export interface PipelineLimits {
  readonly maxPages?: number;
  readonly pageSize?: number;
  readonly maxItems?: number;
  readonly [key: string]: number | string | undefined;
}

/** Mirrors `PipelineModel`: a standing harvest of a source, identified by container, source and scope key. */
export interface PipelineModel {
  readonly id: string;
  readonly containerId: string;
  readonly sourceId: string;
  /** `*` for the whole source. */
  readonly scopeKey: string;
  readonly scope: {
    readonly url?: string;
    readonly restrictToPath?: boolean;
    readonly parameters: Readonly<Record<string, string>>;
  };
  readonly name?: string;
  readonly state: PipelineState;
  readonly pauseReason?: PipelinePauseReason;
  readonly pinned: boolean;
  readonly ai: AiMode;
  readonly limits: PipelineLimits;
  readonly steps: Readonly<Record<string, Readonly<Record<string, unknown>>>>;
  readonly schedules: readonly ScheduleModel[];
  readonly createdAtUtc: string;
}
