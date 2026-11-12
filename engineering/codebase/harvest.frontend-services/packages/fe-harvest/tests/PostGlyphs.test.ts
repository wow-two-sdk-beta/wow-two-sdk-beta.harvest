import { describe, expect, it } from 'vitest';
import { Glyphs, kindGlyphOf, sectionGlyphOf } from '../src/admin/harvestConsole/PostGlyphs';
import { sourceSections } from '../src/admin/harvestConsole/SourceSections';
import type { SourceCapabilitiesModel } from '../src/contracts';

describe('post pill glyphs', () => {
  it('draws a post kind with its own glyph, the catch-all for any other', () => {
    expect(kindGlyphOf('event')).toBe('event');
    expect(kindGlyphOf('article')).toBe('article');
    expect(kindGlyphOf('listing')).toBe('offer');
    expect(kindGlyphOf('other')).toBe('other');
    expect(kindGlyphOf('anything-new')).toBe('other');
  });

  it('reads a section’s glyph from the topic its name carries', () => {
    expect(sectionGlyphOf('Exhibitions')).toBe('exhibition');
    expect(sectionGlyphOf('Fashion')).toBe('fashion');
    expect(sectionGlyphOf('Theatre')).toBe('theatre');
    expect(sectionGlyphOf('Concerts')).toBe('concert');
    expect(sectionGlyphOf('Events')).toBe('event');
    expect(sectionGlyphOf('Whole source')).toBe('section');
  });

  it('has a drawn path for every glyph name', () => {
    for (const name of [kindGlyphOf('event'), sectionGlyphOf('Fashion'), 'language', 'history'] as const)
      expect(Glyphs[name]).toMatch(/^M/);
  });
});

describe('section names and languages', () => {
  const capabilities = (sections: object[]) =>
    ({
      supportsPathScope: true,
      configuration: { scopeSchema: { sections } },
    }) as unknown as SourceCapabilitiesModel;

  it('keeps the name apart from the language when a page is listed in two', () => {
    const [russian, uzbek, theatre] = sourceSections(
      capabilities([
        { label: 'Fashion', url: 'https://example.test/ru/fashion', locale: 'ru' },
        { label: 'Fashion', url: 'https://example.test/uz/fashion', locale: 'uz' },
        { label: 'Theatre', url: 'https://example.test/ru/theatre', locale: 'ru' },
      ]),
    );
    expect(russian).toMatchObject({ name: 'Fashion', label: 'Fashion · Russian', locale: 'ru' });
    expect(uzbek).toMatchObject({ name: 'Fashion', label: 'Fashion · Uzbek', locale: 'uz' });
    expect(theatre).toMatchObject({ name: 'Theatre', label: 'Theatre', locale: 'ru' });
  });
});
