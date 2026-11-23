import type { RunModel } from '../../contracts';

/** Whether a run is still waiting or working. */
export function isActiveRun(run: RunModel) {
  return run.status === 'queued' || run.status === 'running' || run.status === 'paused';
}

/** The partial endings that are a run's own budget running out, not something going wrong. */
const normalStops = new Set(['page_limit', 'item_limit']);

/**
 * Whether a run needs an operator's eye: it failed, was interrupted, or ended partial for a reason other than its page
 * or item limit. A run that stops at its limit is a normal stop (Afisha's 5-page harvests end that way every time).
 */
export function isFailedRun(run: RunModel) {
  if (run.status === 'partial') return !normalStops.has(run.completionReason ?? '');
  return run.status === 'failed' || run.status === 'interrupted';
}
