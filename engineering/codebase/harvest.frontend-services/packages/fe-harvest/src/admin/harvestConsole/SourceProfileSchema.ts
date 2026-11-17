import type { PipelineLimits } from '../../contracts';

type ProfileLimitKey = 'pageSize' | 'maxPages' | 'maxItems';

/** A declarative numeric bound supported by the profile form. @internal */
export interface ProfileLimit {
  readonly key: ProfileLimitKey;
  readonly title: string;
  readonly description?: string;
  readonly minimum: number;
  readonly maximum: number;
  readonly default: number;
  readonly legacyProduct?: readonly { readonly key: ProfileLimitKey; readonly default: number }[];
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
  const keys = ['pageSize', 'maxPages', 'maxItems'] as const;
  for (const key of keys) {
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
    let legacyProduct: ProfileLimit['legacyProduct'];
    if (bound.legacyProduct !== undefined) {
      if (!Array.isArray(bound.legacyProduct) || bound.legacyProduct.length < 1 || bound.legacyProduct.length > 4)
        return undefined;
      const factors: { key: ProfileLimitKey; default: number }[] = [];
      for (const value of bound.legacyProduct) {
        const factor = record(value);
        if (
          !factor ||
          !keys.includes(factor.key as ProfileLimitKey) ||
          typeof factor.default !== 'number' ||
          !Number.isSafeInteger(factor.default) ||
          factor.default < 1
        )
          return undefined;
        factors.push({ key: factor.key as ProfileLimitKey, default: factor.default });
      }
      legacyProduct = factors;
    }
    limits.push({
      key,
      minimum,
      maximum,
      default: fallback,
      title: typeof bound.title === 'string' ? bound.title : key,
      ...(typeof bound.description === 'string' ? { description: bound.description } : {}),
      ...(legacyProduct ? { legacyProduct } : {}),
    });
  }
  return {
    title: typeof url.title === 'string' ? url.title : 'Profile URL',
    ...(typeof url.description === 'string' ? { description: url.description } : {}),
    ...(fixedUrl ? { fixedUrl } : {}),
    limits,
  };
}

/** Reads a saved bound or its adapter-declared conversion from older limit fields. @internal */
export function profileLimitValue(bound: ProfileLimit, limits: PipelineLimits | undefined): number | string {
  const saved = limits?.[bound.key];
  if (saved !== undefined) return saved;
  if (!bound.legacyProduct) return bound.default;
  let value = 1;
  for (const factor of bound.legacyProduct) {
    const part = limits?.[factor.key] ?? factor.default;
    if (typeof part !== 'number' || !Number.isSafeInteger(part) || part < 1) return bound.default;
    value *= part;
  }
  return Number.isSafeInteger(value) ? Math.min(bound.maximum, Math.max(bound.minimum, value)) : bound.default;
}
