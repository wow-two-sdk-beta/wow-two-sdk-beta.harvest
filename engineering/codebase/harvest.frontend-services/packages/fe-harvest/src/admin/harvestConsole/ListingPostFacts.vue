<script setup lang="ts">
import { computed } from 'vue';
import type { ListingProjection } from '../../contracts';
import { areaOf, floorOf, listPriceOf, placeOfListing, wordOf, yearOf } from './ListingWords';

const props = defineProps<{ listing: ListingProjection; description?: string }>();
const offer = computed(() => props.listing.offers?.[0]);
const price = computed(() => listPriceOf(offer.value?.price, offer.value?.period));
const previous = computed(() => listPriceOf(offer.value?.previousPrice));
const place = computed(() => {
  const location = props.listing.place;
  if (!location) return [];
  return [...new Set([placeOfListing(location), location.address].filter(Boolean))];
});
const buildYearLabel = computed(() => {
  switch (props.listing.property.buildYear?.role) {
    case 'builtOrHandover':
      return 'Built or handed over';
    case 'handover':
      return 'Handover';
    default:
      return 'Year built';
  }
});
const terms = computed(() =>
  [
    {
      label: 'Previous price',
      value:
        previous.value &&
        (offer.value?.previousPrice?.currency ? previous.value : `${previous.value} · currency unstated`),
    },
    {
      label: 'Commission',
      value: offer.value?.hasCommission == null ? undefined : offer.value.hasCommission ? 'Applies' : 'None',
    },
    { label: 'Negotiable', value: offer.value?.negotiable == null ? undefined : offer.value.negotiable ? 'Yes' : 'No' },
  ].filter((item): item is { label: string; value: string } => !!item.value),
);
const propertyFacts = computed(() => {
  const { unit, property } = props.listing;
  return [
    { label: 'Property', value: wordOf(property.kind) },
    { label: 'Market', value: wordOf(property.marketSegment) },
    { label: 'Rooms', value: unit.rooms },
    { label: 'Floor', value: floorOf(unit.floor, unit.buildingFloors) },
    { label: 'Total area', value: areaOf(unit.totalAreaM2) },
    { label: 'Living area', value: areaOf(unit.livingAreaM2) },
    { label: 'Kitchen area', value: areaOf(unit.kitchenAreaM2) },
    { label: 'Lot area', value: areaOf(unit.lotAreaM2) },
    { label: 'Layout', value: [wordOf(unit.layout), wordOf(unit.roomConnectivity)].filter(Boolean).join(' · ') },
    { label: 'Construction', value: wordOf(property.construction) },
    { label: buildYearLabel.value, value: yearOf(property.buildYear) },
    {
      label: 'Bathroom',
      value: unit.bathroomsMin == null ? wordOf(unit.bathroomArrangement) : `${unit.bathroomsMin}+`,
    },
    { label: 'Ceiling height', value: unit.ceilingHeightM == null ? undefined : `${unit.ceilingHeightM} m` },
    { label: 'Finish', value: wordOf(property.finishing) },
    { label: 'Renovation', value: wordOf(property.renovation) },
    { label: 'Furnishing', value: wordOf(unit.furnishing) },
  ].filter((item) => (typeof item.value === 'number' && Number.isFinite(item.value)) || !!item.value);
});

const scopeNames = { unit: 'In the unit', building: 'In the building', complex: 'In the complex', nearby: 'Nearby' };
const featureGroups = computed(() =>
  Object.entries(scopeNames)
    .map(([scope, label]) => ({
      label,
      items: (props.listing.features ?? [])
        .filter((feature) => feature.scope === scope && feature.present)
        .map((feature) => wordOf(feature.code)),
    }))
    .filter((group) => group.items.length),
);
const flagWords: Readonly<Record<string, string>> = {
  floorExceedsBuildingFloors: 'The stated floor exceeds the building’s floor count.',
  livingAreaExceedsTotalArea: 'The stated living area exceeds the total.',
  livingPlusKitchenExceedsTotalArea: 'The stated living and kitchen areas together exceed the total.',
  implausibleApartmentArea: 'The stated total area is unusually large for an apartment.',
  priceRangeReversed: 'The stated price range is reversed.',
  priceCurrencyConflict: 'The source supplies different currencies for the asking price.',
  priceAmountConflict: 'The source supplies different amounts for the asking price.',
  buildYearConflict: 'Construction and handover values differ.',
  unconfirmedZeroPrice: 'The publisher did not confirm that this offer is free.',
};
const flags = computed(() => (props.listing.qualityFlags ?? []).map((flag) => flagWords[flag] ?? wordOf(flag)));
const unknown = computed(() => props.listing.unknownValues ?? []);
function sourceLabel(path: string) {
  const leaf = path.split('/').pop()?.replace(/~1/g, '/').replace(/~0/g, '~').replace(/_/g, ' ');
  return wordOf(leaf);
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-5">
    <div
      v-if="price || terms.length || offer?.transaction || place.length"
      class="grid items-start gap-4 lg:grid-cols-2"
    >
      <section
        v-if="price || terms.length || offer?.transaction"
        aria-label="Offer"
        class="rounded-2xl border border-border bg-card p-5"
      >
        <h2 class="text-sm font-semibold text-muted-foreground">
          {{ offer?.transaction === 'rent' ? 'Rent' : offer?.transaction === 'sale' ? 'Asking price' : 'Offer' }}
        </h2>
        <p v-if="price" class="mt-1 text-3xl font-semibold tracking-tight break-words tabular-nums">{{ price }}</p>
        <dl v-if="terms.length" class="mt-3 grid grid-cols-[minmax(0,8rem)_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
          <template v-for="term in terms" :key="term.label">
            <dt class="text-muted-foreground">{{ term.label }}</dt>
            <dd class="min-w-0 font-medium break-words">{{ term.value }}</dd>
          </template>
        </dl>
      </section>
      <section v-if="place.length" aria-label="Location" class="rounded-2xl border border-border bg-card p-5">
        <h2 class="text-sm font-semibold text-muted-foreground">Location</h2>
        <p v-for="line in place" :key="line" class="mt-1 break-words">{{ line }}</p>
      </section>
    </div>
    <section v-if="propertyFacts.length" aria-label="Property" class="rounded-2xl border border-border bg-card p-5">
      <h2 class="text-lg font-semibold">Property</h2>
      <dl class="mt-3 grid grid-cols-2 gap-x-4 gap-y-4 sm:grid-cols-3 xl:grid-cols-4">
        <div v-for="fact in propertyFacts" :key="fact.label" class="min-w-0">
          <dt class="text-[13px] text-muted-foreground">{{ fact.label }}</dt>
          <dd class="mt-0.5 font-medium break-words">{{ fact.value }}</dd>
        </div>
      </dl>
    </section>
    <section v-if="featureGroups.length" aria-label="Features" class="rounded-2xl border border-border bg-card p-5">
      <h2 class="text-lg font-semibold">Features</h2>
      <div class="mt-3 grid gap-4 sm:grid-cols-2">
        <div v-for="group in featureGroups" :key="group.label">
          <h3 class="text-sm font-medium text-muted-foreground">{{ group.label }}</h3>
          <ul class="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-sm">
            <li v-for="item in group.items" :key="item">{{ item }}</li>
          </ul>
        </div>
      </div>
    </section>
    <section v-if="description?.trim()" aria-label="Description" class="rounded-2xl border border-border bg-card p-5">
      <h2 class="text-lg font-semibold">Description</h2>
      <p class="mt-2 max-w-prose text-[15px] leading-6 wrap-anywhere whitespace-pre-line">{{ description }}</p>
    </section>
    <details v-if="flags.length || unknown.length" class="rounded-2xl border border-border bg-card text-sm">
      <summary class="cursor-pointer rounded-2xl px-5 py-3 font-semibold">Source values needing review</summary>
      <div class="flex flex-col gap-3 border-t border-border p-5">
        <ul v-if="flags.length" class="list-disc space-y-1 pl-4">
          <li v-for="flag in flags" :key="flag">{{ flag }}</li>
        </ul>
        <dl v-if="unknown.length" class="grid gap-x-4 gap-y-2 sm:grid-cols-[minmax(0,11rem)_minmax(0,1fr)]">
          <template v-for="(item, index) in unknown" :key="index">
            <dt class="break-words text-muted-foreground">{{ sourceLabel(item.sourcePath) }}</dt>
            <dd class="min-w-0 break-words">{{ item.raw }}</dd>
          </template>
        </dl>
      </div>
    </details>
  </div>
</template>
