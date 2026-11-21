import type { PipelineAutoOffModel, ScheduleModel } from '../../contracts';
import type { DiscoveryWalk } from '../../contracts/PipelineStagesModel';
import { clockOf, plural } from './SourceWords';

export const Weekdays = [
  { value: 1, label: 'Mon', name: 'Monday' },
  { value: 2, label: 'Tue', name: 'Tuesday' },
  { value: 3, label: 'Wed', name: 'Wednesday' },
  { value: 4, label: 'Thu', name: 'Thursday' },
  { value: 5, label: 'Fri', name: 'Friday' },
  { value: 6, label: 'Sat', name: 'Saturday' },
  { value: 0, label: 'Sun', name: 'Sunday' },
] as const;

export interface CalendarSchedule {
  repeat: 'daily' | 'weekly' | 'monthly';
  time: string;
  weekdays: number[];
  monthDay: string;
}

export function defaultCalendar(): CalendarSchedule {
  return { repeat: 'daily', time: '09:00', weekdays: [1], monthDay: '1' };
}

/** The calendar fields cover one time of day, selected weekdays, or a day in each month. */
export function readCalendar(cron?: string): CalendarSchedule | undefined {
  const parts = /^(\d{1,2})\s+(\d{1,2})\s+(\*|L|\d{1,2})\s+\*\s+(\*|[0-7](?:,[0-7])*)$/i.exec(cron?.trim() ?? '');
  if (!parts) return undefined;
  const [, minute, hour, day, weekdays] = parts;
  if (Number(minute) > 59 || Number(hour) > 23 || (day !== '*' && weekdays !== '*')) return undefined;
  const calendar = defaultCalendar();
  calendar.time = `${hour!.padStart(2, '0')}:${minute!.padStart(2, '0')}`;
  if (weekdays !== '*') {
    calendar.repeat = 'weekly';
    calendar.weekdays = [...new Set(weekdays!.split(',').map((value) => Number(value) % 7))];
  } else if (day !== '*') {
    calendar.repeat = 'monthly';
    calendar.monthDay = day!.toUpperCase() === 'L' ? 'L' : String(Number(day));
  }
  return calendar;
}

export function calendarCron(calendar: CalendarSchedule) {
  const time = /^([01]\d|2[0-3]):([0-5]\d)$/.exec(calendar.time);
  if (!time) return undefined;
  const minute = Number(time[2]);
  const hour = Number(time[1]);
  const dates = calendar.repeat === 'monthly' ? calendar.monthDay : '*';
  const days =
    calendar.repeat === 'weekly'
      ? Weekdays.filter((day) => calendar.weekdays.includes(day.value))
          .map((day) => day.value)
          .join(',')
      : '*';
  if (!days || (dates !== '*' && dates !== 'L' && !/^(?:[1-9]|[12]\d|3[01])$/.test(dates))) return undefined;
  return `${minute} ${hour} ${dates} * ${days}`;
}

/** Reads the two duration forms the registry and the editor exchange. */
export function intervalMinutes(value?: string) {
  const count = (part?: string) => Number(part ?? 0);
  const clock = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(value ?? '');
  if (clock) return count(clock[1]) * 1440 + count(clock[2]) * 60 + count(clock[3]) + count(clock[4]) / 60;
  const iso = /^P(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+(?:\.\d+)?)S)?)?$/.exec(value ?? '');
  return iso ? count(iso[1]) * 1440 + count(iso[2]) * 60 + count(iso[3]) + count(iso[4]) / 60 : undefined;
}

function intervalWords(value?: string) {
  const minutes = intervalMinutes(value);
  if (!minutes) return value ?? '1 hour';
  if (minutes < 1) return `${Math.round(minutes * 60)} s`;
  if (minutes % 1440 === 0) return plural(minutes / 1440, 'day');
  if (minutes % 60 === 0) return plural(minutes / 60, 'hour');
  return `${Number(minutes.toFixed(2))} min`;
}

function calendarWords(schedule: ScheduleModel) {
  const calendar = readCalendar(schedule.cron);
  if (!calendar) return `Custom calendar (${schedule.timeZone})`;
  const repeat =
    calendar.repeat === 'weekly'
      ? Weekdays.filter((day) => calendar.weekdays.includes(day.value))
          .map((day) => day.label)
          .join(', ')
      : calendar.repeat === 'monthly'
        ? calendar.monthDay === 'L'
          ? 'Last day of each month'
          : `Day ${calendar.monthDay} of each month`
        : 'Daily';
  return `${repeat} at ${calendar.time} (${schedule.timeZone})`;
}

export function scheduleWords(schedule?: ScheduleModel) {
  if (!schedule || schedule.mode === 'manual') return 'On demand';
  const cadence = schedule.mode === 'cron' ? calendarWords(schedule) : `Every ${intervalWords(schedule.every)}`;
  return [cadence, schedule.stage?.startsOn ? `From ${schedule.stage.startsOn}` : '', schedule.enabled ? '' : 'Off']
    .filter(Boolean)
    .join(' · ');
}

export function isAutomaticSchedule(schedule: ScheduleModel) {
  return schedule.enabled && schedule.mode !== 'manual' && schedule.stage?.disabled !== true;
}

/** What a turned-off pipeline says: the count it took, and the run that reached it with when. */
export function autoOffWords(autoOff: PipelineAutoOffModel) {
  return {
    words: `Turned off after ${plural(autoOff.failures, 'failed run')}`,
    detail: `Last run ended ${autoOff.reason.replaceAll('_', ' ')} · ${clockOf(autoOff.atUtc)}`,
  };
}

export function postBatchLimit(kind: 'harvest' | 'process', model?: string) {
  if (kind === 'harvest') return 500;
  return model?.trim() ? 20 : 100;
}

/** The shortest interval a schedule of a kind may run at, in seconds: 15 s for harvest, 5 minutes for every other kind. */
export function minimumInterval(kind: ScheduleModel['kind']) {
  return kind === 'harvest' ? 15 : 300;
}

/** The longest request gap a stage may set, in ms. */
export const MaxGapMs = 60_000;
/** The most parallel fetches a harvest stage may run. */
export const MaxParallel = 16;

/** How a discover stage walks its catalog, in words. */
export function walkWords(walk?: DiscoveryWalk) {
  if (walk === 'full') return 'Whole catalog each run';
  if (walk === 'new') return 'Newest pages until a known page';
  return 'Resumes its cursor';
}

/** The gap between a stage's requests, in words: its own, or the host's pace. */
export function gapWords(gapMs?: number) {
  return gapMs === undefined ? 'Host pace' : `${gapMs} ms apart`;
}

/** The parallel fetches of a harvest stage, in words. */
export function parallelWords(parallel: number) {
  return `${parallel} at once`;
}

export function nextAutomaticSchedule(schedules: readonly ScheduleModel[]) {
  return schedules
    .filter((schedule) => isAutomaticSchedule(schedule) && schedule.nextDueAtUtc)
    .sort((a, b) => Date.parse(a.nextDueAtUtc!) - Date.parse(b.nextDueAtUtc!))[0];
}
