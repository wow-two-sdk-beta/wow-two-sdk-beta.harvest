import { describe, expect, it } from 'vitest';
import {
  ageOf,
  agoOf,
  eventDatesOf,
  changedWords,
  checkChoiceOf,
  harvestTimesText,
  intervalWords,
  lastHarvestNote,
  lastHarvestText,
  paceWords,
  runFoundWords,
  runProblem,
  timingOf,
} from '../src/admin/harvestConsole/SourceWords';
import type { RunModel, SourceHarvestModel } from '../src/contracts';

const run = (change: Partial<RunModel>): RunModel => ({
  id: 'r',
  containerId: 'c',
  pipelineId: 'p',
  kind: 'capture',
  status: 'completed',
  queuedAtUtc: '2026-11-10T08:00:00Z',
  captured: 4,
  unchanged: 0,
  discovered: 23,
  failed: 0,
  pages: 5,
  requests: 5,
  bytes: 1,
  ...change,
});

describe('source words', () => {
  it('splits a run problem into a short label and its own sentence', () => {
    expect(runProblem(run({ status: 'failed', fetchOutcome: 'timeout', message: 'the host did not answer' }))).toEqual({
      label: 'timed out',
      sentence: 'The host did not answer.',
    });
    expect(runProblem(run({ status: 'partial', fetchOutcome: 'challenge' }))).toEqual({
      label: 'bot check',
      sentence: '',
    });
    expect(runProblem(run({}))).toBeUndefined();
  });

  it('says what a run found and why it stopped', () => {
    expect(runFoundWords(run({ completionReason: 'page_limit', status: 'partial' }), 5)).toBe(
      '4 new of 23 found · stopped at the 5-page limit',
    );
    expect(runFoundWords(run({ kind: 'refresh', captured: 1, unchanged: 0 }))).toBe('1 post changed');
    expect(runFoundWords(run({ status: 'failed', captured: 0, discovered: 0 }))).toBe('nothing harvested');
  });

  it('reads schedules, paces and revisions in words', () => {
    expect(intervalWords('PT6H')).toBe('every 6 hours');
    expect(intervalWords('PT1H')).toBe('every hour');
    expect(intervalWords('P1D')).toBe('daily');
    expect(intervalWords('P7D')).toBe('weekly');
    expect(checkChoiceOf('P1D')).toBe('daily');
    expect(checkChoiceOf(undefined)).toBe('off');
    expect(paceWords(5000)).toBe('a page every 5 s');
    expect(changedWords(['created'])).toBe('first harvest');
    expect(changedWords(['location', 'dates'])).toBe('the place and the dates changed');
  });

  it('writes the harvest line and pill of a never-harvested source', () => {
    expect(harvestTimesText({})).toBe('never harvested');
    expect(lastHarvestText(undefined)).toBe('no harvest yet');
  });

  it('counts how long ago something happened, rounding down', () => {
    const now = new Date('2026-11-12T12:00:00Z');
    const ago = (value: string) => agoOf(value, now);
    expect(ago('2026-11-12T11:59:40Z')).toBe('just now');
    expect(ago('2026-11-12T11:55:00Z')).toBe('5 min ago');
    expect(ago('2026-11-12T11:00:00Z')).toBe('1 hour ago');
    expect(ago('2026-11-12T08:12:00Z')).toBe('3 hours ago');
    expect(ago('2026-11-11T11:00:00Z')).toBe('1 day ago');
    expect(ago('2026-11-10T08:00:00Z')).toBe('2 days ago');
    expect(ago('2026-09-01T08:00:00Z')).toBe('72 days ago');
    expect(ago('2025-11-13T08:00:00Z')).toBe('364 days ago');
    expect(ago('2025-11-12T08:00:00Z')).toBe('1 year ago');
    expect(ago('2023-11-01T08:00:00Z')).toBe('3 years ago');
    expect(agoOf(undefined, now)).toBe('—');
  });

  it('marks an event ongoing or finished only from its stated end, a day-only end running to the close of that day', () => {
    const now = new Date('2026-11-12T12:00:00Z');
    expect(timingOf('2026-10-01', '2026-11-30', now)).toBe('ongoing');
    expect(timingOf('2026-11-01', '2026-11-12', now)).toBe('ongoing');
    expect(timingOf('2026-11-01', '2026-11-11', now)).toBe('finished');
    expect(timingOf('2026-11-20', '2026-11-22', now)).toBeUndefined();
    expect(timingOf('2026-10-01', undefined, now)).toBeUndefined();
  });

  it('writes the last-harvest pill as one short line, the icon standing for "last harvest" and the tint for a partial one', () => {
    const now = new Date('2026-11-12T12:00:00Z');
    const harvest = (change: Partial<SourceHarvestModel>): SourceHarvestModel => ({
      runId: 'r',
      pipelineId: 'p',
      at: '2026-11-12T08:12:00Z',
      status: 'completed',
      newPosts: 4,
      found: 23,
      ...change,
    });
    expect(lastHarvestText(harvest({}), now)).toBe('3 hours ago · +4 new · 23 found');
    expect(lastHarvestText(harvest({ status: 'partial', newPosts: 0 }), now)).toBe('3 hours ago · 0 new · 23 found');
    expect(lastHarvestText(harvest({ status: 'failed' }), now)).toBe('3 hours ago · +4 new · 23 found · failed');
    expect(lastHarvestNote(harvest({ status: 'partial', reason: 'page_limit' }))).toBe('partial · page limit');
    expect(lastHarvestNote(harvest({}))).toBe('');
    expect(lastHarvestText(harvest({ status: 'running' }), now)).toBe('harvesting now');
  });

  it('counts the same age without the word ago, for a label with its own icon', () => {
    const now = new Date('2026-11-12T12:00:00Z');
    expect(ageOf('2026-11-12T11:55:00Z', now)).toBe('5 min');
    expect(ageOf('2026-11-12T08:12:00Z', now)).toBe('3 hours');
    expect(ageOf('2026-11-11T11:00:00Z', now)).toBe('1 day');
    expect(ageOf('2025-11-12T08:00:00Z', now)).toBe('1 year');
    expect(ageOf('2026-11-12T11:59:50Z', now)).toBe('just now');
    expect(agoOf('2026-11-12T11:59:50Z', now)).toBe('just now');
  });

  it('writes an event’s dates as one day or a range, with the year only when it is not this one', () => {
    const now = new Date('2026-11-12T12:00:00Z');
    const dates = (start?: string, end?: string) => eventDatesOf(start, end, now);
    expect(dates('2026-11-21')).toBe('21 Nov');
    expect(dates('2026-11-21T19:00', '2026-11-21')).toBe('21 Nov');
    expect(dates('2026-11-21', '2026-11-23')).toBe('21–23 Nov');
    expect(dates('2026-11-28T10:00', '2026-12-02')).toBe('28 Nov – 2 Dec');
    expect(dates('2026-12-28', '2027-01-03')).toBe('28 Dec – 3 Jan 2027');
    expect(dates('2026-12-30', '2027-01-01')).toBe('30 Dec – 1 Jan 2027');
    expect(dates('2025-12-28', '2026-01-03')).toBe('28 Dec – 3 Jan');
    expect(dates('2026-03-05', '2027-06-10')).toBe('5 Mar 2026 – 10 Jun 2027');
    expect(dates('2026-11-24', '2027-01-24')).toBe('24 Nov – 24 Jan 2027');
    expect(dates('2025-10-01', '2025-10-12')).toBe('1–12 Oct 2025');
    expect(dates('2027-03-05')).toBe('5 Mar 2027');
    expect(dates('2026-11')).toBe('Nov');
    expect(dates('2026-11-21', '2026-11-20')).toBe('21 Nov');
    expect(dates('2026-11-21', '2026-11')).toBe('21 Nov');
    expect(dates('2026-12-19T18:30:00+05:00')).toBe('19 Dec');
    expect(dates(undefined, '2026-11-23')).toBeUndefined();
    expect(dates('soon')).toBeUndefined();
  });
});
