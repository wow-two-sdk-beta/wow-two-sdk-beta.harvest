/** Where an instance stands with one registered source: `new` was never turned on, `on` collects, `off` was turned off. */
export type CatalogSourceState = 'new' | 'on' | 'off';

/** What a registered source collects: the events of a publisher, or the listings of a marketplace site. */
export type CatalogSourceKind = 'events' | 'listings';

/** One address a catalog entry reads: how it is read (`site`, `telegram`, …) and its handle. */
export interface CatalogSourceHostModel {
  readonly family: string;
  readonly handle: string;
}

/** A source the entry set up on this instance: its id, the container that holds it and its display name. */
export interface CatalogSourceSetUpModel {
  readonly sourceId: string;
  readonly containerId: string;
  readonly name: string;
}

/**
 * Mirrors `CatalogSourceModel`: one source the image registers — a publisher or a site — with whether this instance
 * turned it on. Operators read the whole catalog; the key names the entry, such as `ocharo.afisha`.
 */
export interface CatalogSourceModel {
  readonly key: string;
  /** The catalog the entry belongs to: `ocharo` or `marketplace`. */
  readonly catalog: string;
  readonly name: string;
  readonly description?: string | null;
  readonly kind: CatalogSourceKind;
  readonly hosts: readonly CatalogSourceHostModel[];
  /** How many channels or searches turn on and off with the entry. */
  readonly channels: number;
  readonly state: CatalogSourceState;
  /** When the entry was last switched; omitted for an entry nobody switched. */
  readonly changedAtUtc?: string | null;
  readonly changedBy?: string | null;
  /** The sources the entry set up on this instance; empty while the entry is `new`. */
  readonly sources: readonly CatalogSourceSetUpModel[];
}

/** The body of the switch: turn the entry on or off. */
export interface CatalogSourceSwitch {
  readonly turnedOn: boolean;
}
