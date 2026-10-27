import type { ApiKeyState, TenantState } from './RegistrationEnums';

/** Mirrors `TenantModel`: a consuming service that holds keys, containers and subscriptions. */
export interface TenantModel {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly state: TenantState;
  readonly platformCredentialsAllowed: boolean;
  /** ISO 8601 timestamp (UTC). */
  readonly createdAtUtc: string;
}

/** Mirrors `ApiKeyModel`: an issued key's prefix and metadata, never its secret. */
export interface ApiKeyModel {
  readonly id: string;
  readonly tenantId: string;
  readonly name: string;
  readonly prefix: string;
  readonly scopes: readonly string[];
  readonly state: ApiKeyState;
  readonly createdAtUtc: string;
  readonly expiresAtUtc?: string;
  readonly lastUsedAtUtc?: string;
  readonly revokedAtUtc?: string;
}
