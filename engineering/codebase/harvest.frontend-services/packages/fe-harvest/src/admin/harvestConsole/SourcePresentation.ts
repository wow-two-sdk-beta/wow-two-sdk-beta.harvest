import type { SourceOverviewModel, SourceState } from '../../contracts';
import { readMethodOf, type ReadMethod } from './SourceWords';

/** Visible source-channel labels, shared by source cards and the detail header. */
export const SourceTypeLabels: Record<ReadMethod, string> = {
  site: 'Website',
  telegram: 'Telegram',
  instagram: 'Instagram',
  social: 'Social network',
  branch: 'Branch',
};

/** Names a source's channel without replacing its publisher logo. */
export function sourceTypeLabel(family: string) {
  return SourceTypeLabels[readMethodOf(family)];
}

/** Only the active/paused gate can be changed; availability states belong to the reader. */
export function sourcePauseReason(state?: SourceState, supported = true) {
  if (!supported) return 'Source enablement is unavailable on this server version.';
  if (!state || state === 'active' || state === 'paused') return '';
  const reason = {
    unavailable: 'unavailable',
    previewUnavailable: 'unavailable for preview',
    quotaExhausted: 'out of reading quota',
    retired: 'retired',
  }[state];
  return `This source is ${reason}. Its reading state cannot be changed here.`;
}

/** Separates source enablement from work currently running and host health. */
export function sourceStateLabel(source: SourceOverviewModel) {
  if (source.sourceState === 'paused') return 'Disabled';
  if (source.sourceState === 'unavailable') return 'Unavailable';
  if (source.sourceState === 'previewUnavailable') return 'Preview unavailable';
  if (source.sourceState === 'quotaExhausted') return 'Quota exhausted';
  if (source.sourceState === 'retired') return 'Retired';
  if (source.permission === 'restricted' || source.permission === 'denied') return 'Access required';
  if (source.state === 'needsAttention') return 'Needs attention';
  if (source.state === 'harvesting') return 'Running';
  if (source.state === 'healthy') return 'Idle';
  if (!source.sections.length) return 'Not configured';
  return source.sections.some((section) => !section.paused) ? 'Manual' : 'Sections paused';
}
