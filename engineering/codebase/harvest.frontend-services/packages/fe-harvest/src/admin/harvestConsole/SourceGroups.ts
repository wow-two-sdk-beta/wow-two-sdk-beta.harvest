import type { SourceGroupModel, SourceOverviewModel } from '../../contracts';
import { type PillTone, statusWords } from './SourceWords';

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

/** The latest outcomes to surface beside a group's new post count, in priority order. @internal */
const GroupStatuses = ['failed', 'interrupted', 'partial', 'cancelled', 'running', 'queued'] as const;

/** A group's harvest count and latest outcome. */
export function groupHarvestPill(sources: readonly SourceOverviewModel[]): { text: string; tone: PillTone } {
  const harvested = sources.flatMap((source) => (source.lastHarvest ? [source.lastHarvest] : []));
  if (!harvested.length) return { text: 'no harvest yet', tone: 'dashed' };
  const fresh = harvested.reduce((sum, harvest) => sum + harvest.newPosts, 0);
  const count = fresh ? `+${fresh} new` : 'nothing new';
  const status = GroupStatuses.find((status) => harvested.some((harvest) => harvest.status === status));
  return { text: status ? `${count} · ${statusWords(status)}` : count, tone: 'outline' };
}
