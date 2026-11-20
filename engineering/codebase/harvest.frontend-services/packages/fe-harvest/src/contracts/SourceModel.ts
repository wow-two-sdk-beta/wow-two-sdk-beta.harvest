import type {
  ConsentBasis,
  ModelProcessingMode,
  RobotsPolicy,
  SourceCompleteness,
  SourceCoverage,
  SourceFamily,
  SourcePermission,
  SourcePosture,
  SourceState,
} from './RegistrationEnums';

/** Mirrors `SourceModel`: where posts come from, with its posture switches. The capability policy stays opaque here. */
export interface SourceModel {
  readonly id: string;
  readonly containerId: string;
  readonly family: SourceFamily;
  readonly handle: string;
  /** The display name the console shows; omitted, it shows the handle. */
  readonly name?: string;
  /** The group the source belongs to; omitted when it has none. */
  readonly groupId?: string;
  readonly externalRef?: string;
  /** The catalog entry that set the source up; omitted or null for a source registered by hand. */
  readonly catalogKey?: string | null;
  /** False while the source's catalog entry is off on this instance; such a source collects, processes and delivers nothing. */
  readonly turnedOn: boolean;
  readonly coverage: SourceCoverage;
  readonly completeness: SourceCompleteness;
  readonly posture: SourcePosture;
  readonly permission: SourcePermission;
  /** Whether a model may read the source's text at all: `modelProcessing` is not `off`. */
  readonly aiAllowed: boolean;
  /** How a model may process the source's posts: `off`, `manual` (operator runs only) or `auto`. */
  readonly modelProcessing: ModelProcessingMode;
  readonly poolShareable: boolean;
  readonly robotsPolicy: RobotsPolicy;
  readonly consentBasis: ConsentBasis;
  readonly authority: number;
  readonly retentionClass: string;
  readonly checkPolicy?: string;
  readonly capabilityPolicy: Readonly<Record<string, unknown>>;
  readonly state: SourceState;
  readonly stateSinceUtc: string;
  readonly createdAtUtc: string;
}
