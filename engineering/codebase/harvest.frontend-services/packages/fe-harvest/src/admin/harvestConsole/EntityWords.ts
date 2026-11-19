import type { EntityPostModel, EntitySummaryModel, OccurrenceModel } from '../../contracts';
import { eventDatesOf, type PillTone } from './SourceWords';

const Months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** How often an entity runs, in words. */
export function cadenceWords(cadence: string) {
  const words: Record<string, string> = {
    annual: 'Every year',
    semiannual: 'Twice a year',
    quarterly: 'Every quarter',
    monthly: 'Every month',
    irregular: 'Irregular',
    unknown: 'Cadence unknown',
  };
  return words[cadence] ?? cadence;
}

/** A taxonomy key's last node, as words: `style-fashion/womens-fashion` reads `Womens fashion`. */
export function domainWords(key?: string) {
  const node = key?.split('/').at(-1);
  if (!node) return undefined;
  const words = node.replace(/-/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** An entity type in words. */
export function entityKindWords(type: string) {
  return type === 'event' ? 'Event' : type === 'listing' ? 'Listing' : type;
}

/** A `yyyy-MM-dd` day at a precision: `Nov 2026` for a month, `2026` for a year, else the day's dates. */
function dayAt(value: string, precision: string) {
  const [year, month] = value.split('-');
  if (precision === 'year') return year ?? value;
  if (precision === 'month') return month ? `${Months[Number(month) - 1] ?? month} ${year}` : value;
  return undefined;
}

/** An occurrence's dates as precise as they are known: `21–23 Nov 2026`, `Oct 2026`, `2026`. */
export function occurrenceDates(occurrence: Pick<OccurrenceModel, 'starts' | 'ends' | 'precision'>, now = new Date()) {
  const coarse = dayAt(occurrence.starts, occurrence.precision);
  if (coarse) return coarse;
  return eventDatesOf(occurrence.starts, occurrence.ends, now) ?? occurrence.starts;
}

/** The tone of an occurrence's status. */
export function occurrenceTone(occurrence: Pick<OccurrenceModel, 'status' | 'predicted'>): PillTone {
  if (occurrence.predicted) return 'dashed';
  if (occurrence.status === 'cancelled') return 'danger';
  if (occurrence.status === 'postponed') return 'warning';
  if (occurrence.status === 'held') return 'neutral';
  return 'success';
}

/** An occurrence's status in words; a predicted one reads `predicted`. */
export function occurrenceStatusWords(occurrence: Pick<OccurrenceModel, 'status' | 'predicted'>) {
  return occurrence.predicted ? 'predicted' : occurrence.status;
}

/** An entity's counts in one line: `3 runs · 5 posts · 2 sources`. */
export function countsText(entity: Pick<EntitySummaryModel, 'occurrences' | 'posts' | 'sources'>) {
  const count = (value: number, word: string) => `${value} ${value === 1 ? word : `${word}s`}`;
  return [count(entity.occurrences, 'run'), count(entity.posts, 'post'), count(entity.sources, 'source')].join(' · ');
}

/** A post's role in words, with what an update changed: `update: dates, place`. */
export function roleWords(post: Pick<EntityPostModel, 'role' | 'changes'>) {
  return post.role === 'update' && post.changes.length ? `update: ${post.changes.join(', ')}` : post.role;
}

/** A role's pill tone. */
export function roleTone(role: string): PillTone {
  if (role === 'announcement') return 'info';
  if (role === 'update') return 'warning';
  if (role === 'report') return 'success';
  return 'neutral';
}

/** Groups posts by the year they are about, newest year first, keeping each year's order. */
export function postsByYear(posts: readonly EntityPostModel[]) {
  const years = new Map<number, EntityPostModel[]>();
  for (const post of posts) years.set(post.year, [...(years.get(post.year) ?? []), post]);
  return years;
}

/** The years a list of entities covers, newest first. */
export function yearsOf(entities: readonly Pick<EntitySummaryModel, 'years'>[]) {
  return [...new Set(entities.flatMap((entity) => entity.years.map((year) => year.year)))].sort((a, b) => b - a);
}

/** The month a `yyyy-MM-dd` day falls in, as a heading: `November 2026`. */
export function monthHeading(day: string) {
  const [year, month] = day.split('-');
  const names = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];
  return `${names[Number(month) - 1] ?? month} ${year}`;
}

/** A local date as `yyyy-MM-dd`. */
export function isoDay(date: Date) {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
