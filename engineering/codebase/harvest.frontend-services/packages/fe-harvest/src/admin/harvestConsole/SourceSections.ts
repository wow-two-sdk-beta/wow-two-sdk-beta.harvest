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

/** Uses section metadata only on a runtime that enforces its path. */
export function sourceSections(capabilities?: SourceCapabilitiesModel) {
  const sections = capabilities?.configuration.scopeSchema.sections;
  return capabilities?.supportsPathScope === true && Array.isArray(sections) ? sections.filter(isSourceSection) : [];
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
