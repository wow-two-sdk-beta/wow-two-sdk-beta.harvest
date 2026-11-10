import { describe, expect, it } from 'vitest';
import type { SourceCapabilitiesModel } from '../src/contracts';
import { sourceSections } from '../src/admin/harvestConsole/SourceSections';

function capabilities(sections: unknown[]) {
  return {
    sourceId: 'source',
    workKinds: ['capture'],
    supportsPathScope: true,
    configuration: { displayName: 'Afisha', readiness: 'ready', scopeSchema: { sections } },
  } as unknown as SourceCapabilitiesModel;
}

describe('source sections', () => {
  it('names a label listed in two languages by its language, and leaves a single label alone', () => {
    const sections = sourceSections(
      capabilities([
        { label: 'Fashion', url: 'https://www.afisha.uz/ru/fashion', locale: 'ru' },
        { label: 'Fashion', url: 'https://www.afisha.uz/uz/fashion', locale: 'uz' },
        { label: 'Theatre', url: 'https://www.afisha.uz/ru/theatre', locale: 'ru' },
      ]),
    );

    expect(sections.map((item) => item.label)).toEqual(['Fashion · Russian', 'Fashion · Uzbek', 'Theatre']);
  });
});
