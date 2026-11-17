import { describe, expect, it } from 'vitest';
import { factsOf, floorOf, listPriceOf, listingOf } from '../src/admin/harvestConsole/ListingWords';

describe('housing listing display', () => {
  it('keeps unit floor and total floors separate, including ground level', () => {
    expect(factsOf({ rooms: 2, totalAreaM2: 51, floor: 0, buildingFloors: 9 })).toEqual([
      '2 rooms',
      '51 m²',
      'Floor 0 of 9',
    ]);
    expect(floorOf(undefined, 9)).toBe('9 building floors');
    expect(factsOf({})).toEqual([]);
  });

  it('shows a published currency or preserves the original unresolved label', () => {
    expect(listPriceOf({ amount: 600, currency: 'USD' }, 'month')).toBe('600 USD / month');
    expect(listPriceOf({ amount: 600, raw: '600 у.е.' })).toBe('600 у.е.');
    expect(listPriceOf({ amount: 0, currency: 'UZS' })).toBe('0 UZS');
  });

  it('accepts only the housing projection when reading older post extras', () => {
    expect(listingOf({ schema: 'olx.ad.v1', property: {}, unit: {} })).toBeUndefined();
    expect(listingOf({ schema: 'wow2.listing.v1', property: {}, unit: { floor: 2 } })?.unit.floor).toBe(2);
  });
});
