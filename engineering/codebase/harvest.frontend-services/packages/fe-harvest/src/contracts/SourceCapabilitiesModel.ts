import type { WorkKind } from './RegistrationEnums';
import type { SourcePostFilterDefinition } from './SourcePostQuery';

/** Safe, runtime-local adapter discovery; readiness never promises remote access. */
export interface SourceCapabilitiesModel {
  readonly sourceId: string;
  readonly adapterKey?: string;
  readonly supportsPathScope?: boolean;
  readonly workKinds: readonly WorkKind[];
  readonly configuration: {
    readonly displayName: string;
    readonly readiness: 'ready' | 'configurationRequired' | 'unavailable';
    readonly message?: string;
    readonly scopeSchema: Readonly<Record<string, unknown>>;
    readonly sourceFieldsSchema?: Readonly<Record<string, unknown>>;
    readonly postFilters?: readonly SourcePostFilterDefinition[];
    readonly postView?: { readonly showPhotos?: boolean; readonly showSourceText?: boolean };
  };
}
