<script setup lang="ts">
import { computed } from 'vue';
import type { SourceListingSummary } from '../../contracts';
import { factsOf, listPriceOf, placeOfListing } from './ListingWords';

const props = defineProps<{ listing: SourceListingSummary; onImage?: boolean }>();
const price = computed(() => listPriceOf(props.listing.price, props.listing.period));
const facts = computed(() => factsOf(props.listing));
const place = computed(() => placeOfListing(props.listing));
const transaction = computed(() => {
  switch (props.listing.transaction) {
    case 'rent':
      return 'For rent';
    case 'sale':
      return 'For sale';
    case 'exchange':
      return 'For exchange';
    default:
      return undefined;
  }
});
</script>

<template>
  <div
    v-if="price || facts.length || place || transaction"
    class="min-w-0"
    :class="onImage ? 'space-y-1' : 'flex flex-wrap items-baseline gap-x-3 gap-y-0.5'"
    v-bind="{ 'data-listing-preview': '' }"
  >
    <div v-if="price || transaction" class="flex flex-wrap items-baseline gap-x-2">
      <p
        v-if="price"
        class="min-w-0 font-semibold break-words tabular-nums"
        :class="onImage ? 'text-xl text-white' : 'text-sm'"
      >
        {{ price }}
      </p>
      <p v-if="transaction" class="text-xs" :class="onImage ? 'text-white/80' : 'text-muted-foreground'">
        {{ transaction }}
      </p>
    </div>
    <ul
      v-if="facts.length"
      class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 text-[13px]"
      :class="onImage ? 'font-medium text-white' : 'text-muted-foreground'"
      aria-label="Property facts"
    >
      <li v-for="fact in facts" :key="fact">{{ fact }}</li>
    </ul>
    <p v-if="place" class="truncate text-[13px]" :class="onImage ? 'text-white/80' : 'text-muted-foreground'">
      {{ place }}
    </p>
  </div>
</template>
