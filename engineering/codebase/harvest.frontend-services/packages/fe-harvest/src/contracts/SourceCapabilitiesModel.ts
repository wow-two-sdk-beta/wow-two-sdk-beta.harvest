import type { WorkKind } from './RegistrationEnums';

/** Safe, runtime-local adapter discovery; readiness never promises remote access. */
export interface SourceCapabilitiesModel {
  readonly sourceId: string;
  readonly adapterKey?: string;
  readonly workKinds: readonly WorkKind[];
  readonly configuration: {
    readonly displayName: string;
    readonly readiness: 'ready' | 'configurationRequired' | 'unavailable';
    readonly message?: string;
    readonly scopeSchema: Readonly<Record<string, unknown>>;
  };
}
