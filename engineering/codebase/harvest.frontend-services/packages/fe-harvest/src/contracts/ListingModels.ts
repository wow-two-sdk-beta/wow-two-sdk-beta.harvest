/** Housing fields shipped with a source's post list. */
export interface SourceListingSummary {
  readonly propertyKind?: string;
  readonly rooms?: number;
  readonly totalAreaM2?: number;
  readonly floor?: number;
  readonly buildingFloors?: number;
  readonly city?: string;
  readonly district?: string;
  readonly transaction?: string;
  readonly period?: string;
  readonly price?: ListingMoney;
}

/** Decimal amounts use the source's stated currency and price basis. */
export interface ListingMoney {
  readonly amount?: number;
  readonly min?: number;
  readonly max?: number;
  readonly currency?: string;
  readonly raw?: string;
}

/** The versioned housing extra of a resolved post. */
export interface ListingProjection {
  readonly schema: 'wow2.listing.v1';
  readonly property: {
    readonly kind?: string;
    readonly marketSegment?: string;
    readonly construction?: string;
    readonly buildYear?: { readonly from?: number; readonly to?: number; readonly role: string };
    readonly finishing?: string;
    readonly renovation?: string;
  };
  readonly unit: {
    readonly rooms?: number;
    readonly floor?: number;
    readonly buildingFloors?: number;
    readonly totalAreaM2?: number;
    readonly livingAreaM2?: number;
    readonly kitchenAreaM2?: number;
    readonly lotAreaM2?: number;
    readonly ceilingHeightM?: number;
    readonly bathroomArrangement?: string;
    readonly bathroomsMin?: number;
    readonly furnishing?: string;
    readonly layout?: string;
    readonly roomConnectivity?: string;
  };
  readonly offers?: readonly ListingOffer[];
  readonly place?: {
    readonly city?: string;
    readonly district?: string;
    readonly address?: string;
    readonly latitude?: number;
    readonly longitude?: number;
  };
  readonly features?: readonly { readonly code: string; readonly scope: string; readonly present: boolean }[];
  readonly publisher?: { readonly accountClass?: string; readonly verified?: boolean };
  readonly qualityFlags?: readonly string[];
  readonly unknownValues?: readonly {
    readonly path: string;
    readonly sourcePath: string;
    readonly raw: string;
    readonly reason: string;
  }[];
}

export interface ListingOffer {
  readonly transaction?: string;
  readonly period?: string;
  readonly price?: ListingMoney;
  readonly previousPrice?: ListingMoney;
  readonly negotiable?: boolean;
  readonly hasCommission?: boolean;
}
