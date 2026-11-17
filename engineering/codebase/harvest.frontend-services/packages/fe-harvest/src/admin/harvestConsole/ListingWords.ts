import type { ListingMoney, ListingProjection, SourceListingSummary } from '../../contracts';

const NumberOf = new Intl.NumberFormat(undefined, { maximumFractionDigits: 6 });

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

export function listingOf(extra: unknown): ListingProjection | undefined {
  if (!extra || typeof extra !== 'object') return undefined;
  const candidate = extra as Partial<ListingProjection>;
  return candidate.schema === 'wow2.listing.v1' && candidate.property && candidate.unit
    ? (candidate as ListingProjection)
    : undefined;
}

export function listPriceOf(value?: ListingMoney, period?: string) {
  if (!value) return undefined;
  const withCurrency = (amount: number) => [NumberOf.format(amount), value.currency].filter(Boolean).join(' ');
  let text = value.raw?.trim() || undefined;
  if (!text || value.currency) {
    if (isNumber(value.amount)) text = withCurrency(value.amount);
    else if (isNumber(value.min) && isNumber(value.max) && value.min <= value.max)
      text = `${NumberOf.format(value.min)}–${withCurrency(value.max)}`;
    else if (isNumber(value.min) && !isNumber(value.max)) text = `From ${withCurrency(value.min)}`;
    else if (isNumber(value.max) && !isNumber(value.min)) text = `Up to ${withCurrency(value.max)}`;
  }
  return text && period ? `${text} / ${period}` : text;
}

export function floorOf(floor?: number, floors?: number) {
  if (isNumber(floor)) return isNumber(floors) ? `Floor ${floor} of ${floors}` : `Floor ${floor}`;
  return isNumber(floors) ? `${floors} building floors` : undefined;
}

export function areaOf(value?: number) {
  return isNumber(value) ? `${NumberOf.format(value)} m²` : undefined;
}

export function factsOf(listing: SourceListingSummary) {
  return [
    isNumber(listing.rooms) ? `${listing.rooms} ${listing.rooms === 1 ? 'room' : 'rooms'}` : undefined,
    areaOf(listing.totalAreaM2),
    floorOf(listing.floor, listing.buildingFloors),
  ].filter((item): item is string => !!item);
}

export function placeOfListing(listing: Pick<SourceListingSummary, 'city' | 'district'>) {
  return [...new Set([listing.district, listing.city].filter(Boolean))].join(' · ');
}

export function wordOf(value?: string) {
  if (!value) return undefined;
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^[a-z]/, (letter) => letter.toUpperCase());
}

export function yearOf(year?: ListingProjection['property']['buildYear']) {
  if (!year) return undefined;
  const { from, to } = year;
  if (isNumber(from)) return isNumber(to) && to !== from ? `${from}–${to}` : String(from);
  return isNumber(to) ? `Through ${to}` : undefined;
}
