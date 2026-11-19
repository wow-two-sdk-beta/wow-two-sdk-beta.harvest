import { AiBrandNames, aiModelLabel, resolveAiBrand } from '@wow-two-beta/fe/domain/aiBrands';
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

/** A brand's name as people write it: `Claude` for a Claude model, else the provider key as given. */
export function providerName(provider?: string, model?: string) {
  const brand = resolveAiBrand({ provider, model });
  return brand ? AiBrandNames[brand] : provider || 'Unknown provider';
}

/** A model id as a person reads it: `claude-haiku-5-5` reads `Claude Haiku 5.5`; an unknown id stays as given. */
export function modelLabel(model?: string) {
  return aiModelLabel(model);
}

/** What names an answer's mark: the model label led by the brand unless it already starts with it (`OpenAI GPT-5 mini`). */
export function markLabel(provider?: string, model?: string) {
  const brand = providerName(provider, model);
  const label = modelLabel(model);
  if (!label) return brand;
  return label.toLowerCase().startsWith(brand.toLowerCase()) ? label : `${brand} ${label}`;
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

/** A token count as short as its size allows: `186`, `1.4k`, `12k`. */
export function tokenCount(count: number) {
  if (count < 1000) return String(count);
  const thousands = count / 1000;
  return `${thousands < 10 ? thousands.toFixed(1).replace(/\.0$/, '') : Math.round(thousands)}k`;
}

/** An answer's tokens, input and output together: `1.4k tok`; nothing when the inference log recorded none. */
export function tokensText(answer: ProcessedAnswerModel) {
  const { inputTokens, outputTokens } = answer;
  const known = [inputTokens, outputTokens].filter((count): count is number => typeof count === 'number');
  return known.length ? `${tokenCount(known.reduce((sum, count) => sum + count, 0))} tok` : undefined;
}

/** An answer's tokens split by direction, for a tooltip: `1,240 in · 186 out`. */
export function tokensDetail(answer: ProcessedAnswerModel) {
  const format = new Intl.NumberFormat('en-US');
  return [
    typeof answer.inputTokens === 'number' ? `${format.format(answer.inputTokens)} in` : undefined,
    typeof answer.outputTokens === 'number' ? `${format.format(answer.outputTokens)} out` : undefined,
  ]
    .filter(Boolean)
    .join(' · ');
}

/** One answer's provenance in one line: `haiku-5-5 · $0.0003 · 1.8s · 1.4k tok · p2`. */
export function answerLine(answer: ProcessedAnswerModel) {
  return [
    shortModel(answer.model) ?? 'no model',
    costText(answer.costMicroUsd),
    latencyText(answer.latencyMs),
    tokensText(answer),
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

/** One model's share of a card's answers: its label and the passes it answered, each with its cost, speed and tokens. */
export interface AnswerGroup {
  readonly provider?: string;
  readonly model?: string;
  /** The brand-led model label, as the mark is named. */
  readonly label: string;
  readonly passes: readonly { readonly name: string; readonly text: string }[];
}

/**
 * The card footer's answers grouped by the model that gave them: tags and extraction on one model make one group, on
 * two models two, each with its own mark. A pass with no cost, latency or tokens adds no text.
 */
export function answerGroups(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel): AnswerGroup[] {
  const answers = [
    { name: 'tags', answer: tags.answer },
    ...(extraction ? [{ name: 'extraction', answer: extraction.answer }] : []),
  ];
  const groups = new Map<string, { provider?: string; model?: string; passes: { name: string; text: string }[] }>();
  for (const { name, answer } of answers) {
    const key = `${answer.provider ?? ''}/${answer.model ?? ''}`;
    const group = groups.get(key) ?? { provider: answer.provider, model: answer.model, passes: [] };
    groups.set(key, group);
    const text = [costText(answer.costMicroUsd), latencyText(answer.latencyMs), tokensText(answer)]
      .filter(Boolean)
      .join(' · ');
    if (text) group.passes.push({ name, text });
  }
  return [...groups.values()].map((group) => ({ ...group, label: markLabel(group.provider, group.model) }));
}

/** When a card was last answered: the later of its tags' and extraction's answers. */
export function answeredAt(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  return [tags.answer.answeredAt, extraction?.answer.answeredAt]
    .filter((value): value is string => !!value)
    .sort()
    .at(-1);
}

/** The provider a card shows: the tags answer's, else the extraction's. */
export function cardProvider(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  return tags.answer.provider ?? extraction?.answer.provider;
}

/** The model of the answer `cardProvider` names. */
export function cardModel(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  return tags.answer.provider ? tags.answer.model : extraction?.answer.model;
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

/** The flags behind `unsureFields`, one per field: the field, why it is unsure and the value proposed for it. */
export function unsureFlags(tags: ProcessedTagsModel, extraction?: ProcessedExtractionModel) {
  const flags = new Map<string, { field: string; reason: string; proposed?: string }>();
  for (const flag of [...tags.flags, ...(extraction?.flags ?? [])]) {
    if (flag.field && !flags.has(flag.field))
      flags.set(flag.field, {
        field: flag.field,
        reason: flag.reason,
        ...(flag.proposed ? { proposed: flag.proposed } : {}),
      });
  }
  return [...flags.values()];
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
