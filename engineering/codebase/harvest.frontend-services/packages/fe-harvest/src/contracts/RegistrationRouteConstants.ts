/** Mirrors `RegistrationRouteConstants` of the be contract, as absolute paths. */
export const RegistrationRouteConstants = {
  tenants: '/api/tenants',
  tenantApiKeys: 'api-keys',
  apiKeys: '/api/api-keys',
  containers: '/api/containers',
  sources: '/api/sources',
  pipelines: '/api/pipelines',
  tenantIdParameter: 'tenantId',
  containerIdParameter: 'containerId',
  /** The service's status probe; it needs no key. */
  systemStatus: '/api/system/status',
} as const;
