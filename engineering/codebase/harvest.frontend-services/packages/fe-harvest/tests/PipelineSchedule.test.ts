import { describe, expect, it } from 'vitest';
import {
  autoOffWords,
  calendarCron,
  defaultCalendar,
  readCalendar,
  scheduleWords,
} from '../src/admin/harvestConsole/PipelineSchedule';
import { clockOf } from '../src/admin/harvestConsole/SourceWords';

describe('pipeline calendar schedules', () => {
  it('keeps the selected weekdays and local run time through a round trip', () => {
    const calendar = { ...defaultCalendar(), repeat: 'weekly' as const, weekdays: [1, 3, 0], time: '07:05' };
    const cron = calendarCron(calendar);
    expect(cron).toBe('5 7 * * 1,3,0');
    expect(readCalendar(cron)).toEqual(calendar);
    expect(calendarCron({ ...calendar, weekdays: [] })).toBeUndefined();
  });

  it('describes the last day of each month with its date and zone', () => {
    const calendar = { ...defaultCalendar(), repeat: 'monthly' as const, monthDay: 'L' };
    expect(calendarCron(calendar)).toBe('0 9 L * *');
    expect(
      scheduleWords({
        id: 'schedule',
        kind: 'discover',
        mode: 'cron',
        cron: calendarCron(calendar)!,
        timeZone: 'Asia/Tashkent',
        enabled: false,
        stage: { batchSize: 3, startsOn: '2026-11-20' },
      }),
    ).toBe('Last day of each month at 09:00 (Asia/Tashkent) · From 2026-11-20 · Off');
    expect(readCalendar('*/15 7-18 * * 1-5')).toBeUndefined();
  });
});

describe('a pipeline turned off after failed runs', () => {
  const autoOff = { atUtc: '2026-11-18T09:30:00Z', runId: 'run', failures: 10, reason: 'blocked' };

  it('says how many failed runs it took and what the last one ended on', () => {
    expect(autoOffWords(autoOff)).toEqual({
      words: 'Turned off after 10 failed runs',
      detail: `Last run ended blocked · ${clockOf(autoOff.atUtc)}`,
    });
  });

  it('reads a single failure in the singular and an outcome with its underscores as words', () => {
    const words = autoOffWords({ ...autoOff, failures: 1, reason: 'rate_limited' });
    expect(words.words).toBe('Turned off after 1 failed run');
    expect(words.detail).toMatch(/^Last run ended rate limited · /);
  });
});
