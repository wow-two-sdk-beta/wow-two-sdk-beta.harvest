import type { RunModel, WorkKind } from '../../contracts';
import type { PillTone } from './SourceWords';
import { plural } from './SourceWords';

export const WorkLabels: Record<WorkKind, string> = {
  discover: 'Discover',
  harvest: 'Harvest',
  process: 'Process',
  capture: 'Full harvest',
  refresh: 'Refresh',
  sweep: 'Sweep',
  check: 'Check',
  metrics: 'Metrics',
  link: 'Link',
  intake: 'Intake',
};

export const RunTones = {
  queued: 'info',
  running: 'info',
  completed: 'success',
  partial: 'warning',
  failed: 'danger',
  cancelled: 'neutral',
  interrupted: 'warning',
  paused: 'warning',
} as const satisfies Record<RunModel['status'], PillTone>;

/** Prefer work already started over a newer run waiting in the queue. */
export function activeRun(runs: readonly RunModel[]) {
  return runs.find((run) => run.status === 'running') ?? runs.find((run) => run.status === 'queued');
}

export function needsAttention(run?: RunModel) {
  return (
    !!run && (run.status === 'failed' || run.status === 'interrupted' || run.status === 'paused' || run.failed > 0)
  );
}

export function runCounts(run: RunModel) {
  const counts =
    run.kind === 'discover'
      ? [plural(run.pages, 'page'), `${run.discovered} found`]
      : run.kind === 'process'
        ? [`${run.captured} processed`, `${run.unchanged} unchanged`]
        : [`${run.captured} captured`, `${run.unchanged} unchanged`];
  if (run.failed) counts.push(`${run.failed} failed`);
  return counts.join(' · ');
}
