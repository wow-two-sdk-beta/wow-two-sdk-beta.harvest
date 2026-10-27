import type { ContainerKind, ContainerState } from './RegistrationEnums';

/** Mirrors `ContainerModel`: the visibility boundary — the pool, or one tenant's private container. */
export interface ContainerModel {
  readonly id: string;
  readonly kind: ContainerKind;
  /** Absent for the pool. */
  readonly tenantId?: string;
  readonly externalRef?: string;
  readonly residency?: string;
  readonly embeddingsEnabled: boolean;
  readonly state: ContainerState;
  readonly createdAtUtc: string;
}
