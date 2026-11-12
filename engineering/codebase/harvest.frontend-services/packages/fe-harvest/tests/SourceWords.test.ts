import { describe, expect, it } from 'vitest';
import {
  agoOf,
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
});
