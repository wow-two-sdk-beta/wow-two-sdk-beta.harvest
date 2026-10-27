/** Mirrors `HarvestScopeConstants` and `ApiKeyConstants` of the be contract. */
export const HarvestScopeConstants = {
  tenantsManage: 'tenants:manage',
  containersManage: 'containers:manage',
  sourcesManage: 'sources:manage',
  pipelinesRead: 'pipelines:read',
  pipelinesRun: 'pipelines:run',
  pipelinesManage: 'pipelines:manage',
  subscriptionsManage: 'subscriptions:manage',
  eventsRead: 'events:read',
  linksSubmit: 'links:submit',
  entriesWrite: 'entries:write',
  postsRead: 'posts:read',
} as const;

/** The header an API key travels in, and the marker every Harvest secret starts with. */
export const ApiKeyConstants = {
  headerName: 'X-Api-Key',
  marker: 'hv_',
} as const;
