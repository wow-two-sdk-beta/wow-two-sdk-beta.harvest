/** Mirrors the be contract's registry enums as their camelCase wire labels. */
export type TenantState = 'active' | 'suspended';
export type ApiKeyState = 'active' | 'revoked';
export type ContainerKind = 'pool' | 'private';
export type ContainerState = 'active' | 'archived';
export type SourceFamily =
  | 'page'
  | 'structured'
  | 'site'
  | 'render'
  | 'api'
  | 'feed'
  | 'link'
  | 'listing'
  | 'telegram'
  | 'whatsapp'
  | 'instagram'
  | 'facebook'
  | 'youtube'
  | 'threads'
  | 'x'
  | 'intake'
  | 'manual';
export type SourceCoverage = 'whole' | 'filtered' | 'single';
export type SourceCompleteness = 'complete' | 'window' | 'none';
export type SourcePosture = 'official' | 'licensedProvider' | 'ownFetch' | 'consentedIntake';
export type SourcePermission = 'open' | 'restricted' | 'granted' | 'denied';
export type RobotsPolicy = 'obey' | 'ignore';
export type ConsentBasis = 'owner' | 'submitter' | 'none';
export type SourceState = 'active' | 'paused' | 'unavailable' | 'previewUnavailable' | 'quotaExhausted' | 'retired';
export type PipelineState = 'proposed' | 'pendingApproval' | 'active' | 'paused' | 'archived';
export type PipelinePauseReason = 'operator' | 'budgetHard' | 'permission' | 'idle';
export type WorkKind = 'capture' | 'discover' | 'refresh' | 'sweep' | 'check' | 'metrics' | 'link' | 'intake';
export type ScheduleMode = 'manual' | 'interval' | 'cron' | 'adaptive';
export type AiMode = 'off' | 'gaps' | 'full';
