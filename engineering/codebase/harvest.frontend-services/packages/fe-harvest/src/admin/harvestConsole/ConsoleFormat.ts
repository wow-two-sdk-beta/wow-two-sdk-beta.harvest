import type { RunModel } from '../../contracts';

/** Whether a run is still waiting or working. */
export function isActiveRun(run: RunModel) {
  return run.status === 'queued' || run.status === 'running';
}

/** Whether a run ended without collecting everything it set out to. */
export function isFailedRun(run: RunModel) {
  return run.status === 'failed' || run.status === 'partial' || run.status === 'interrupted';
}
