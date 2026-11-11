import type {
  RunModel,
  SourceHarvestModel,
  SourceHarvestStatus,
  SourceOverviewModel,
  SourceOverviewState,
} from '../../contracts';

/**
 * The words the Sources area speaks: source, group, section, harvest, subscriber. Every engine term — pipeline, host
 * lane, tenant, container, scope names — is turned into one of these before it reaches the screen.
 */

/** The tint of a pill or badge. */
export type PillTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral' | 'dashed' | 'outline';

/** How a source is read, as its icon shows it. */
export type ReadMethod = 'site' | 'telegram' | 'instagram' | 'social' | 'branch';

/** The view a gallery draws: a grid of cards or a bordered list of rows. */
export type GalleryView = 'cards' | 'rows';

const Locale = 'en-GB';
const time = new Intl.DateTimeFormat(Locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
const dayMonth = new Intl.DateTimeFormat(Locale, { day: 'numeric', month: 'short' });
const fullDate = new Intl.DateTimeFormat(Locale, { day: 'numeric', month: 'short', year: 'numeric' });

/** Parses an instant, or nothing for a missing or unreadable one. @internal */
function instant(value?: string) {
  if (!value) return undefined;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? undefined : date;
}

/** Whether two instants fall on the same local day. @internal */
function sameDay(a: Date, b: Date) {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

/** Counts a noun: `1 source`, `3 sources`. */
export function plural(count: number, word: string, many = `${word}s`) {
  return `${count} ${count === 1 ? word : many}`;
}

/** A time of day for today (`13:12`), else the day and time (`9 Nov 13:12`). */
export function clockOf(value?: string, now = new Date()) {
  const date = instant(value);
  if (!date) return '—';
  return sameDay(date, now) ? time.format(date) : `${dayMonth.format(date)} ${time.format(date)}`;
}

/** When something happened, in words: `today 13:12`, `yesterday 05:20`, `9 Nov 13:12`. */
export function whenOf(value?: string, now = new Date()) {
  const date = instant(value);
  if (!date) return '—';
  if (sameDay(date, now)) return `today ${time.format(date)}`;
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  if (sameDay(date, yesterday)) return `yesterday ${time.format(date)}`;
  return `${(date.getFullYear() === now.getFullYear() ? dayMonth : fullDate).format(date)} ${time.format(date)}`;
}

/** A calendar date: `21 Nov 2026`. */
export function dateOf(value?: string) {
  const date = instant(value);
  return date ? fullDate.format(date) : '—';
}

/** A date with its time: `21 Nov 2026, 17:00`; a date-only value keeps the date alone. */
export function dateTimeOf(value?: string) {
  const date = instant(value);
  if (!date) return '—';
  return /T\d{2}:\d{2}/.test(value ?? '') ? `${fullDate.format(date)}, ${time.format(date)}` : fullDate.format(date);
}

/** The date a day of a 14-day trend stands for, oldest first, the last one today. */
export function trendDayOf(index: number, length: number, now = new Date()) {
  const date = new Date(now);
  date.setDate(now.getDate() - (length - 1 - index));
  return date;
}

/** The first date of a 14-day trend: `28 Oct`. */
export function trendStartOf(length: number, now = new Date()) {
  return dayMonth.format(trendDayOf(0, length, now));
}

/** Reads an ISO-8601 duration (`PT6H`, `P1D`, `PT90M`) as minutes; nothing for an unreadable one. */
export function minutesOf(duration?: string) {
  const match = duration?.match(
    /^P(?:(\d+(?:\.\d+)?)W)?(?:(\d+(?:\.\d+)?)D)?(?:T(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?)?$/,
  );
  if (!match || duration === 'P' || duration === 'PT') return undefined;
  return (
    Number(match[1] ?? 0) * 10080 +
    Number(match[2] ?? 0) * 1440 +
    Number(match[3] ?? 0) * 60 +
    Number(match[4] ?? 0) +
    Number(match[5] ?? 0) / 60
  );
}

/** An interval in words: `every 6 hours`, `every hour`, `daily`, `weekly`, `every 30 minutes`. */
export function intervalWords(duration?: string) {
  const minutes = minutesOf(duration);
  if (minutes === undefined) return 'on demand';
  if (minutes === 10080) return 'weekly';
  if (minutes === 1440) return 'daily';
  if (minutes % 1440 === 0) return `every ${minutes / 1440} days`;
  if (minutes === 60) return 'every hour';
  if (minutes % 60 === 0) return `every ${minutes / 60} hours`;
  return `every ${Math.round(minutes)} minutes`;
}

/** How often a section is harvested: `every 6 hours`, or `on demand` without a schedule. */
export function harvestWords(captureEvery?: string) {
  return captureEvery ? intervalWords(captureEvery) : 'on demand';
}

/** How often a section is checked for changes: `checks daily`; nothing when it is not checked. */
export function checkWords(checkEvery?: string) {
  return checkEvery ? `checks ${intervalWords(checkEvery)}` : undefined;
}

/** The check choice a schedule matches: off, daily or weekly. */
export function checkChoiceOf(checkEvery?: string): 'off' | 'daily' | 'weekly' {
  const minutes = minutesOf(checkEvery);
  if (minutes === undefined) return 'off';
  return minutes >= 10080 ? 'weekly' : 'daily';
}

/** The hours a capture schedule runs every, rounded to a whole hour. */
export function hoursOf(captureEvery?: string) {
  const minutes = minutesOf(captureEvery);
  return minutes === undefined ? undefined : Math.max(1, Math.round(minutes / 60));
}

/** How politely a source is read: `a page every 5 s`. */
export function paceWords(minDelayMs: number) {
  const seconds = Math.round(minDelayMs / 100) / 10;
  return `a page every ${seconds} s`;
}

/** Turns an engine code (`page_limit`, `rateLimited`) into plain words. @internal */
function codeWords(code: string) {
  return code
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim()
    .toLowerCase();
}

/** Why a run stopped, in words: `5-page limit`. */
export function reasonWords(reason?: string, maxPages?: number) {
  if (!reason) return undefined;
  if (reason === 'page_limit') return maxPages ? `${maxPages}-page limit` : 'page limit';
  if (reason === 'exhausted' || reason === 'completed') return undefined;
  return codeWords(reason);
}

/** A source's state, as its pill reads. */
export const StateWords: Record<SourceOverviewState, string> = {
  harvesting: 'harvesting',
  healthy: 'healthy',
  needsAttention: 'needs attention',
  idle: 'idle',
};

/** A source state's tint. */
export function stateTone(state: SourceOverviewState): PillTone {
  if (state === 'healthy') return 'success';
  if (state === 'harvesting') return 'info';
  if (state === 'needsAttention') return 'warning';
  return 'neutral';
}

/** A harvest status's tint: done green, partial amber, failed red, in flight blue. */
export function statusTone(status: SourceHarvestStatus | RunModel['status']): PillTone {
  if (status === 'completed') return 'success';
  if (status === 'partial') return 'warning';
  if (status === 'failed' || status === 'interrupted') return 'danger';
  if (status === 'running' || status === 'queued') return 'info';
  return 'neutral';
}

/** A run status in words. */
export function statusWords(status: SourceHarvestStatus | RunModel['status']) {
  return status === 'running' ? 'running' : codeWords(status);
}

/** What the last harvest found, as one pill reads: `last harvest · +4 new of 23 found · partial`. */
export function lastHarvestText(harvest?: SourceHarvestModel) {
  if (!harvest) return 'no harvest yet';
  if (harvest.status === 'running') return 'harvesting now';
  if (harvest.status === 'queued') return 'harvest queued';
  const found = `last harvest · ${harvest.newPosts ? `+${harvest.newPosts}` : '0'} new of ${harvest.found} found`;
  return harvest.status === 'completed' ? found : `${found} · ${statusWords(harvest.status)}`;
}

/** The last-harvest pill's tint; dashed when nothing was harvested yet. */
export function lastHarvestTone(harvest?: SourceHarvestModel): PillTone {
  return harvest ? statusTone(harvest.status) : 'dashed';
}

/** The line under a source's name: `last harvest 13:12 · next 19:12`, or `never harvested`. */
export function harvestTimesText(source: Pick<SourceOverviewModel, 'lastHarvest' | 'nextHarvestAt'>, now = new Date()) {
  const parts = [
    ...(source.lastHarvest ? [`last harvest ${clockOf(source.lastHarvest.at, now)}`] : ['never harvested']),
    ...(source.nextHarvestAt ? [`next ${clockOf(source.nextHarvestAt, now)}`] : []),
  ];
  return parts.join(' · ');
}

/** How a source family is read, as its icon shows it. */
export function readMethodOf(family: string): ReadMethod {
  if (family === 'telegram') return 'telegram';
  if (family === 'instagram') return 'instagram';
  if (family === 'branch') return 'branch';
  if (['whatsapp', 'facebook', 'youtube', 'threads', 'x'].includes(family)) return 'social';
  return 'site';
}

/** The read method in words, for an icon's title. */
export const ReadMethodWords: Record<ReadMethod, string> = {
  site: 'read from its website',
  telegram: 'read from Telegram',
  instagram: 'read from Instagram',
  social: 'read from a social network',
  branch: 'a branch',
};

/** A post kind's chip label: `event` gives `Events`. */
export function kindPlural(kind: string) {
  const word = codeWords(kind || 'post');
  const many = word.endsWith('s') ? word : `${word}s`;
  return many.charAt(0).toUpperCase() + many.slice(1);
}

/** A run's kind in words. */
export function runKindWords(kind: RunModel['kind']) {
  if (kind === 'capture') return 'harvest';
  if (kind === 'check') return 'check';
  if (kind === 'refresh') return 'refresh';
  if (kind === 'discover') return 'find links';
  return codeWords(kind);
}

/** The label a failure's kind reads as, beside its own sentence. @internal */
const OutcomeWords: Readonly<Record<string, string>> = {
  timeout: 'timed out',
  challenge: 'bot check',
  captcha: 'captcha',
  refused: 'refused',
  blocked: 'blocked',
  rateLimited: 'rate limited',
  rate_limited: 'rate limited',
  notFound: 'not found',
  not_found: 'not found',
  httpError: 'site error',
  http_error: 'site error',
};

/** A failure's kind as a short label: `timeout` gives `timed out`. */
export function outcomeWords(outcome: string) {
  return OutcomeWords[outcome] ?? codeWords(outcome);
}

/** Makes one sentence of a message: a capital first letter and a closing stop. @internal */
function sentence(text: string) {
  const trimmed = text.trim();
  if (!trimmed) return '';
  const capital = trimmed.charAt(0).toUpperCase() + trimmed.slice(1);
  return /[.!?…]$/.test(capital) ? capital : `${capital}.`;
}

/** A run's problem split in two: the failure's kind as a small label, and its message as its own sentence. */
export function runProblem(run: Pick<RunModel, 'fetchOutcome' | 'message' | 'status'>) {
  if (!run.fetchOutcome && !run.message) return undefined;
  return {
    label: run.fetchOutcome ? outcomeWords(run.fetchOutcome) : statusWords(run.status),
    sentence: run.message ? sentence(run.message) : '',
  };
}

/** What a run found, in words: `4 new of 23 found · stopped at the 5-page limit`. */
export function runFoundWords(run: RunModel, maxPages?: number) {
  if (run.status === 'queued') return 'waiting to start';
  if (run.status === 'running') return 'reading the source';
  const stop =
    run.completionReason === 'page_limit'
      ? `stopped at the ${reasonWords(run.completionReason, maxPages)}`
      : reasonWords(run.completionReason);
  if (run.status === 'failed' && !run.captured && !run.discovered) return 'nothing harvested';
  if (run.kind === 'capture') {
    return [`${run.captured} new of ${run.discovered} found`, stop].filter(Boolean).join(' · ');
  }
  const changed = run.captured ? `${plural(run.captured, 'post')} changed` : 'nothing changed';
  return [changed, run.unchanged ? `${run.unchanged} unchanged` : '', stop].filter(Boolean).join(' · ');
}

/** The part of a post a changed field names. @internal */
const FieldWords: Readonly<Record<string, string>> = {
  title: 'the title',
  text: 'the text',
  kind: 'the kind',
  author: 'the author',
  prices: 'the offers',
  postedAt: 'the posted date',
  dates: 'the dates',
  location: 'the place',
  contacts: 'the contacts',
  media: 'the images',
  links: 'the links',
  sourceTags: 'the tags',
  tags: 'the tags',
  language: 'the language',
  extras: 'other details',
};

/** What a revision changed, in words: `created` reads `first harvest`, fields `the place and the dates changed`. */
export function changedWords(changed: readonly string[]) {
  if (!changed.length) return 'no recorded change';
  if (changed.includes('created')) return 'first harvest';
  const parts = [...new Set(changed.map((field) => FieldWords[field] ?? codeWords(field)))];
  const list = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}` : parts[0]!;
  return `${list} changed`;
}

/** Where a source can be opened in a browser: its site, its Telegram channel or its Instagram page. */
export function siteUrlOf(family: string, handle: string) {
  if (/^https?:\/\//i.test(handle)) return handle;
  const name = handle.replace(/^@/, '');
  if (family === 'telegram') return `https://t.me/${name}`;
  if (family === 'instagram') return `https://www.instagram.com/${name}`;
  return /^[\w.-]+\.[a-z]{2,}(\/.*)?$/i.test(handle) ? `https://${handle}` : undefined;
}

/** A URL without its scheme, for a menu row: `www.afisha.uz/ru/fashion`. */
export function shortUrl(url: string) {
  return url.replace(/^https?:\/\//i, '').replace(/\/$/, '');
}

/**
 * An image address the console may draw: a public web address, or an inline raster or SVG image. Anything else —
 * another scheme, embedded credentials, an unreadable value — gives nothing, so no image block shows.
 */
export function imageSrcOf(value?: string) {
  if (!value) return undefined;
  if (/^data:image\/(png|jpeg|gif|webp|avif|svg\+xml)[;,]/i.test(value)) return value;
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? url.href : undefined;
  } catch {
    return undefined;
  }
}
