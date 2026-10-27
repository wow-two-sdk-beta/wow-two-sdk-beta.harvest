import type {
  ConsentBasis,
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
  readonly externalRef?: string;
  readonly coverage: SourceCoverage;
  readonly completeness: SourceCompleteness;
  readonly posture: SourcePosture;
  readonly permission: SourcePermission;
  readonly aiAllowed: boolean;
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
