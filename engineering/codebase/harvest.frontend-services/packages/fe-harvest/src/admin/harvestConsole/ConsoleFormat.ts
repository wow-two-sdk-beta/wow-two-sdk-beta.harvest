import { BadgeVariant } from '@wow-two-beta/fe/presentation/display';
import type { PipelineModel, RunModel } from '../../contracts';

/** Formats an instant in the reader's locale, or a dash when none is recorded. */
export function formatDate(value?: string) {
  if (!value) return '—';
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? value : instant.toLocaleString();
}

/** Describes a pipeline's cadence in one line. */
export function scheduleSummary(pipeline: PipelineModel) {
  return (
    pipeline.schedules
      .map(
        (schedule) =>
          `${schedule.kind} · ${schedule.enabled && schedule.mode !== 'manual' ? (schedule.cron ?? schedule.every ?? schedule.mode) : 'manual only'}`,
      )
      .join(' / ') || 'Manual runs only'
  );
}

/** Whether a run is still waiting or working. */
export function isActiveRun(run: RunModel) {
  return run.status === 'queued' || run.status === 'running';
}

/** Whether a run ended without collecting everything it set out to. */
export function isFailedRun(run: RunModel) {
  return run.status === 'failed' || run.status === 'partial' || run.status === 'interrupted';
}

/** Picks the badge tint that reads a run's status at a glance. */
export function runVariant(run: RunModel): BadgeVariant {
  if (run.status === 'completed') return BadgeVariant.Success;
  if (run.status === 'partial') return BadgeVariant.Warning;
  if (run.status === 'failed' || run.status === 'interrupted') return BadgeVariant.Danger;
  if (isActiveRun(run)) return BadgeVariant.Info;
  return BadgeVariant.Neutral;
}

/** Picks the badge tint for a pipeline, source or subscription state. */
export function stateVariant(state: string): BadgeVariant {
  if (state === 'active' || state === 'live') return BadgeVariant.Success;
  if (state === 'paused' || state === 'suspect' || state === 'pendingApproval') return BadgeVariant.Warning;
  if (state === 'gone' || state === 'unavailable' || state === 'retired') return BadgeVariant.Danger;
  return BadgeVariant.Neutral;
}
