import type { SourceGroupModel, SourceOverviewModel } from '../../contracts';
import { statusTone, type PillTone } from './SourceWords';

/** One publisher on the Sources page: its group, or `Other` for sources filed under none. */
export interface GalleryGroup {
  /** The group's id, or `other`. */
  readonly key: string;
  /** The element id its section carries, which its tile and the menu scroll to. */
  readonly elementId: string;
  readonly name: string;
  readonly description?: string;
  readonly group?: SourceGroupModel;
  /** Every source of the group. */
  readonly sources: readonly SourceOverviewModel[];
}

/** The key of the last group, the sources filed under no publisher. */
export const OtherGroupKey = 'other';

/** The element id of a group's section on the Sources page. */
export function groupElementId(key: string) {
  return `group-${key}`;
}

/** Files sources under their groups in the groups' order; ungrouped sources go in a last group named `Other`. */
export function galleryGroupsOf(groups: readonly SourceGroupModel[], sources: readonly SourceOverviewModel[]) {
  const known = new Set(groups.map((group) => group.id));
  const result: GalleryGroup[] = groups.map((group) => ({
    key: group.id,
    elementId: groupElementId(group.id),
    name: group.name,
    ...(group.description ? { description: group.description } : {}),
    group,
    sources: sources.filter((source) => source.groupId === group.id),
  }));
  const other = sources.filter((source) => !source.groupId || !known.has(source.groupId));
  if (other.length)
    result.push({
      key: OtherGroupKey,
      elementId: groupElementId(OtherGroupKey),
      name: 'Other',
      description: 'Sources not filed under a publisher yet.',
      sources: other,
    });
  return result;
}

/** A group's 14-day trend: its sources' new posts summed per day. */
export function groupTrendOf(sources: readonly SourceOverviewModel[]) {
  const length = Math.max(14, ...sources.map((source) => source.newPostsDaily.length));
  return Array.from({ length }, (_, day) =>
    sources.reduce((sum, source) => {
      const offset = length - source.newPostsDaily.length;
      return sum + (day >= offset ? (source.newPostsDaily[day - offset] ?? 0) : 0);
    }, 0),
  );
}

/** The ways a group's sources are read, each once: site, Telegram, Instagram, branch. */
export function groupFamiliesOf(sources: readonly SourceOverviewModel[]) {
  return [...new Set(sources.map((source) => source.family))];
}

/** Tones from calm to loud, so a group's pill takes its loudest source's. @internal */
const Loudness: readonly PillTone[] = ['neutral', 'info', 'success', 'warning', 'danger'];

/** A group's one pill: `4 new in the last harvest`, or `no harvest yet`. */
export function groupHarvestPill(sources: readonly SourceOverviewModel[]): { text: string; tone: PillTone } {
  const harvested = sources.filter((source) => source.lastHarvest);
  if (!harvested.length) return { text: 'no harvest yet', tone: 'dashed' };
  const fresh = harvested.reduce((sum, source) => sum + (source.lastHarvest?.newPosts ?? 0), 0);
  const tone = harvested
    .map((source) => statusTone(source.lastHarvest!.status))
    .reduce((loudest, tone) => (Loudness.indexOf(tone) > Loudness.indexOf(loudest) ? tone : loudest), 'neutral');
  return { text: fresh ? `${fresh} new in the last harvest` : 'nothing new in the last harvest', tone };
}
