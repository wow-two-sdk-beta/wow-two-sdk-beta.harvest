import type {
  ClassificationVerdict,
  ProcessedAnswerModel,
  ProcessedEventSummary,
  ProcessedExtractionModel,
  ProcessedListingSummary,
  ProcessedOccurrenceModel,
  ProcessedPriceModel,
  ProcessedTagsModel,
} from '../../contracts';
import { eventDatesOf, type PillTone } from './SourceWords';

/** The providers a card draws a mark for; any other provider shares one mark. */
export type ProviderKind = 'anthropic' | 'openai' | 'other';

/** The mark a provider gets: Anthropic, OpenAI, else the shared one. */
export function providerKindOf(provider?: string): ProviderKind {
  const name = provider?.toLowerCase();
  if (name === 'anthropic') return 'anthropic';
  if (name === 'openai') return 'openai';
  return 'other';
}

/** A provider's name as people write it. */
export function providerName(provider?: string) {
  const kind = providerKindOf(provider);
  if (kind === 'anthropic') return 'Anthropic';
  if (kind === 'openai') return 'OpenAI';
  return provider || 'Unknown provider';
}

/** A model id without the family prefix a tooltip has no room for: `claude-haiku-5-5` reads `haiku-5-5`. */
export function shortModel(model?: string) {
  return model?.replace(/^claude-/, '');
}

/** A micro-USD cost in dollars, as short as its size allows: `$0.0003`, `$0.012`, `$1.25`. */
export function costText(costMicroUsd?: number) {
  if (costMicroUsd === undefined || costMicroUsd === null) return undefined;
  const dollars = costMicroUsd / 1_000_000;
  if (dollars === 0) return '$0';
  if (dollars < 0.01) return `$${dollars.toFixed(4)}`;
  if (dollars < 1) return `$${dollars.toFixed(3)}`;
  return `$${dollars.toFixed(2)}`;
}

/** A latency as `850ms` under a second, else `1.8s`. */
export function latencyText(latencyMs?: number) {
  if (latencyMs === undefined || latencyMs === null) return undefined;
  return latencyMs < 1000 ? `${Math.round(latencyMs)}ms` : `${(latencyMs / 1000).toFixed(1)}s`;
}

/** One answer's provenance in one line: `haiku-5-5 · $0.0003 · 1.8s · p2`. */
export function answerLine(answer: ProcessedAnswerModel) {
  return [
    shortModel(answer.model) ?? 'no model',
    costText(answer.costMicroUsd),
    latencyText(answer.latencyMs),
    answer.promptVersion !== undefined && answer.promptVersion !== null ? `p${answer.promptVersion}` : undefined,
  ]
    .filter(Boolean)
    .join(' · ');
}

/** The tooltip's lines: one per answer, the pass named first — `tags haiku-5-5 · …`, `event haiku-5-5 · …`. */
export function provenanceLines(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  const lines = [`tags ${answerLine(tags.answer)}`];
  if (extraction)
    lines.push(`${extraction.useCase === 'extract-listing' ? 'listing' : 'event'} ${answerLine(extraction.answer)}`);
  return lines;
}

/** The provider a card shows: the tags answer's, else the extraction's. */
export function cardProvider(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  return tags.answer.provider ?? extraction?.answer.provider;
}

/** The words of a verdict. */
export const VerdictWords: Record<ClassificationVerdict, string> = {
  relevant: 'Relevant',
  irrelevant: 'Irrelevant',
  uncertain: 'Uncertain',
};

/** The tone of a verdict's pill. */
export function verdictTone(verdict: ClassificationVerdict): PillTone {
  if (verdict === 'relevant') return 'success';
  if (verdict === 'uncertain') return 'warning';
  return 'neutral';
}

/** The words of a tags answer's entity type. */
export function entityTypeWords(type?: string) {
  const words: Record<string, string> = { event: 'Event', listing: 'Listing', none: 'Neither', unsure: 'Unsure' };
  return type ? (words[type] ?? type) : 'No type';
}

/** The fields the model or the checks were unsure of, each once: tags first, then extraction. */
export function unsureFields(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  return [...new Set([...tags.flags, ...(extraction?.flags ?? [])].map((flag) => flag.field).filter(Boolean))];
}

/** A price as `150,000 UZS`, a rent's period after a slash: `500 USD/month`; `free` without an amount. */
export function priceText(price?: ProcessedPriceModel, period = false) {
  if (!price) return undefined;
  const amount =
    price.amount === undefined || price.amount === null
      ? undefined
      : new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(price.amount);
  const value = [amount, price.currency].filter(Boolean).join(' ');
  if (!value) return price.label;
  return period && price.label ? `${value}/${price.label}` : value;
}

/** An occurrence's dates with how they are known: `21–23 Nov 2026 · stated`. */
export function occurrenceText(occurrence: ProcessedOccurrenceModel, now = new Date()) {
  const dates = eventDatesOf(occurrence.starts, occurrence.ends, now) ?? occurrence.starts;
  return [dates, occurrence.certainty].filter(Boolean).join(' · ') || undefined;
}

/** A place from its parts, smallest first. */
export function placeText(...parts: (string | undefined)[]) {
  return parts.filter((part) => part && part.trim()).join(', ') || undefined;
}

/** The lines an event's summary shows: name and edition, dates, place, then format, access and prices. */
export function eventLines(event: ProcessedEventSummary, now = new Date()) {
  const name = [event.name, event.editionLabel].filter(Boolean).join(' · ');
  const dates = event.occurrences.map((occurrence) => occurrenceText(occurrence, now)).filter(Boolean);
  const terms = [
    event.format,
    event.access && event.access !== 'unknown' ? event.access : undefined,
    ...event.prices.map((price) => [priceText(price), price.label].filter(Boolean).join(' ')),
  ].filter(Boolean);
  return [
    ...(name ? [{ key: 'name', text: name }] : []),
    ...(dates.length ? [{ key: 'dates', text: dates.join('; ') }] : []),
    ...(placeText(event.venue, event.city, event.country)
      ? [{ key: 'place', text: placeText(event.venue, event.city, event.country)! }]
      : []),
    ...(terms.length ? [{ key: 'terms', text: terms.join(' · ') }] : []),
  ];
}

/** The lines a listing's summary shows: offer and price, rooms and area, then location. */
export function listingLines(listing: ProcessedListingSummary) {
  const offer = [listing.offerType, priceText(listing.price, listing.offerType === 'rent')].filter(Boolean).join(' · ');
  const unit = [
    listing.rooms !== undefined && listing.rooms !== null
      ? `${listing.rooms} ${listing.rooms === 1 ? 'room' : 'rooms'}`
      : undefined,
    listing.areaM2 !== undefined && listing.areaM2 !== null ? `${listing.areaM2} m²` : undefined,
  ]
    .filter(Boolean)
    .join(' · ');
  const place = placeText(listing.district, listing.city);
  return [
    ...(offer ? [{ key: 'offer', text: offer }] : []),
    ...(unit ? [{ key: 'unit', text: unit }] : []),
    ...(place ? [{ key: 'place', text: place }] : []),
  ];
}
