import { describe, expect, it } from 'vitest';
import {
  changedWords,
  checkChoiceOf,
  harvestTimesText,
  intervalWords,
  lastHarvestText,
  paceWords,
  runFoundWords,
  runProblem,
} from '../src/admin/harvestConsole/SourceWords';
import type { RunModel } from '../src/contracts';

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
});
