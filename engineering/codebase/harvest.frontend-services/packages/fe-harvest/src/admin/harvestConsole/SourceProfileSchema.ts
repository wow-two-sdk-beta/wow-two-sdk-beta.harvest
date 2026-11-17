/** A declarative numeric bound supported by the profile form. @internal */
export interface ProfileLimit {
  readonly key: 'pageSize' | 'maxPages';
  readonly title: string;
  readonly minimum: number;
  readonly maximum: number;
  readonly default: number;
}

/** The safe subset of a source's URL schema that the console can edit. @internal */
export interface SourceProfileSchema {
  readonly title: string;
  readonly description?: string;
  readonly fixedUrl?: string;
  readonly limits: readonly ProfileLimit[];
}

function record(value: unknown): Readonly<Record<string, unknown>> | undefined {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Readonly<Record<string, unknown>>)
    : undefined;
}

/** Catalog-only sources have no custom URL form; labels and bounds come from the adapter. @internal */
export function sourceProfileSchema(
  schema: Readonly<Record<string, unknown>> | undefined,
): SourceProfileSchema | undefined {
  const url = record(record(schema?.properties)?.url);
  if (!Array.isArray(schema?.required) || !schema.required.includes('url') || url?.type !== 'string') return undefined;
  let fixedUrl: string | undefined;
  if (typeof url.const === 'string') {
    try {
      const target = new URL(url.const);
      if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password) return undefined;
      fixedUrl = target.href;
    } catch {
      return undefined;
    }
  }
  const limits: ProfileLimit[] = [];
  const bounds = record(schema.limits);
  for (const key of ['pageSize', 'maxPages'] as const) {
    const bound = record(bounds?.[key]);
    if (!bound) continue;
    const minimum = bound.minimum;
    const maximum = bound.maximum;
    const fallback = bound.default;
    if (
      typeof minimum !== 'number' ||
      !Number.isSafeInteger(minimum) ||
      minimum < 1 ||
      typeof maximum !== 'number' ||
      !Number.isSafeInteger(maximum) ||
      maximum < minimum ||
      typeof fallback !== 'number' ||
      !Number.isSafeInteger(fallback) ||
      fallback < minimum ||
      fallback > maximum
    )
      return undefined;
    limits.push({
      key,
      minimum,
      maximum,
      default: fallback,
      title: typeof bound.title === 'string' ? bound.title : key,
    });
  }
  return {
    title: typeof url.title === 'string' ? url.title : 'Profile URL',
    ...(typeof url.description === 'string' ? { description: url.description } : {}),
    ...(fixedUrl ? { fixedUrl } : {}),
    limits,
  };
}
