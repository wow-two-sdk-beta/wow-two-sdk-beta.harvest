/** Mirrors `RegistrationRouteConstants` of the be contract, as absolute paths. */
export const RegistrationRouteConstants = {
  tenants: '/api/tenants',
  tenantApiKeys: 'api-keys',
  apiKeys: '/api/api-keys',
  containers: '/api/containers',
  sources: '/api/sources',
  /** Every source of a container with its sections, last harvest and health. */
  sourceOverview: '/api/sources/overview',
  /** The publishers sources are grouped under. */
  sourceGroups: '/api/sources/groups',
  /** The sources the image registers and whether this instance turned each on; an entry's key follows the slash. */
  sourceCatalog: '/api/sources/catalog',
  posts: '/api/posts',
  pipelines: '/api/pipelines',
  tenantIdParameter: 'tenantId',
  containerIdParameter: 'containerId',
  /** The service's status probe; it needs no key. */
  systemStatus: '/api/system/status',
} as const;
