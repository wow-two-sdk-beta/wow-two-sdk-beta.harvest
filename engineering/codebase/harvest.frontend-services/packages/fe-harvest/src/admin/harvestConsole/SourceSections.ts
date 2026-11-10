import type { SourceCapabilitiesModel } from '../../contracts';

/** One part of a source an adapter lists, such as a page of upcoming events. */
export interface SourceSection {
  readonly label: string;
  readonly url: string;
  readonly locale?: string;
}

function isSourceSection(value: unknown): value is SourceSection {
  if (!value || typeof value !== 'object') return false;
  const section = value as Partial<SourceSection>;
  return (
    typeof section.label === 'string' &&
    typeof section.url === 'string' &&
    /^https?:\/\//i.test(section.url) &&
    URL.canParse(section.url) &&
    (section.locale === undefined || typeof section.locale === 'string')
  );
}

/** Names a locale in English, such as `ru` → `Russian`; an unknown code reads as itself. @internal */
function languageName(locale: string) {
  try {
    return new Intl.DisplayNames(['en'], { type: 'language' }).of(locale) ?? locale;
  } catch {
    return locale;
  }
}

/**
 * Uses section metadata only on a runtime that enforces its path. A label the adapter lists more than once, such as
 * the same page in two languages, reads with its language (`Fashion · Russian`), so no two sections look alike.
 */
export function sourceSections(capabilities?: SourceCapabilitiesModel): readonly SourceSection[] {
  const listed = capabilities?.configuration.scopeSchema.sections;
  const sections =
    capabilities?.supportsPathScope === true && Array.isArray(listed) ? listed.filter(isSourceSection) : [];
  const repeated = new Set(
    sections.map((item) => item.label).filter((label, index, all) => all.indexOf(label) !== index),
  );
  return sections.map((item) =>
    repeated.has(item.label) && item.locale ? { ...item, label: `${item.label} · ${languageName(item.locale)}` } : item,
  );
}

export function sameSection(url: string, sectionUrl: string) {
  if (!URL.canParse(url)) return false;
  const current = new URL(url);
  const section = new URL(sectionUrl);
  return (
    current.hostname === section.hostname &&
    current.pathname.replace(/\/+$/, '') === section.pathname.replace(/\/+$/, '')
  );
}
