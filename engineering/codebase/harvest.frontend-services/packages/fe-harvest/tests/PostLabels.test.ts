import { describe, expect, it } from 'vitest';
import { labelsStatedBy } from '../src/admin/harvestConsole/PostLabels';

describe('labels a filter already states', () => {
  it('drops the label of a filter with exactly one value selected', () => {
    expect([...labelsStatedBy({ kind: ['event'] })]).toEqual(['kind']);
    expect([...labelsStatedBy({ section: ['fashion'] })].sort()).toEqual(['language', 'section']);
    expect([...labelsStatedBy({ state: ['gone'] })]).toEqual(['removed']);
  });

  it('keeps a label while the filter is empty or holds several values, since it still tells the cards apart', () => {
    expect(labelsStatedBy({ kind: [], section: [] }).size).toBe(0);
    expect(labelsStatedBy({ kind: ['event', 'article'] }).size).toBe(0);
  });

  it('combines filters, and ignores a filter that states no label', () => {
    expect([...labelsStatedBy({ kind: ['event'], section: ['fashion'] })].sort()).toEqual([
      'kind',
      'language',
      'section',
    ]);
    expect(labelsStatedBy({ search: ['tashkent'] }).size).toBe(0);
  });
});
