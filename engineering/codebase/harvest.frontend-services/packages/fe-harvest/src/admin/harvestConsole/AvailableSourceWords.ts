import type { CatalogSourceKind, CatalogSourceModel } from '../../contracts';
import { dateOf, plural } from './SourceWords';

/** The catalog sections of Available, in order, each with the pill and the heading a kind reads as. @internal */
export const CatalogKinds: readonly {
  readonly kind: CatalogSourceKind;
  readonly pill: string;
  readonly heading: string;
}[] = [
  { kind: 'events', pill: 'Events', heading: 'Events' },
  { kind: 'listings', pill: 'Listings', heading: 'Listings' },
];

/** How many handles a line names before it counts the rest. @internal */
const NamedHosts = 3;

/** The entry's addresses as a line: the first handles, then `+2` for the rest. */
export function catalogHostsText(entry: Pick<CatalogSourceModel, 'hosts'>) {
  const handles = entry.hosts.map((host) => host.handle);
  const named = handles.slice(0, NamedHosts).join(', ');
  return handles.length > NamedHosts ? `${named} +${handles.length - NamedHosts}` : named;
}

/** The entry's channel count in words: `3 channels`. */
export function catalogChannelsText(entry: Pick<CatalogSourceModel, 'channels'>) {
  return plural(entry.channels, 'channel');
}

/** Says when an off entry was turned off: `Turned off 18 Nov 2026`, or `Turned off` when the date is unknown. */
export function catalogOffText(entry: Pick<CatalogSourceModel, 'changedAtUtc'>) {
  return entry.changedAtUtc ? `Turned off ${dateOf(entry.changedAtUtc)}` : 'Turned off';
}

/** Whether the entry matches a lower-cased search over its name, key and addresses. */
export function catalogMatches(entry: CatalogSourceModel, query: string) {
  return (
    !query ||
    [entry.name, entry.key, ...entry.hosts.map((host) => host.handle)].some((text) =>
      text.toLowerCase().includes(query),
    )
  );
}
