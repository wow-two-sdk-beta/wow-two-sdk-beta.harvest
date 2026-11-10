<script setup lang="ts">
import { ToggleGroup, ToggleInput } from '@wow-two-beta/fe/presentation/forms';
import { useGalleryView } from '../../application/preferences/UseGalleryView';
import ConsoleIcon from './ConsoleIcon.vue';

/** The bar's Cards | Rows switch for the galleries — Sources and a source's Posts — remembered as one choice. */
defineOptions({ name: 'GalleryViewSwitch' });
const view = useGalleryView();

function choose(value: unknown) {
  if (value === 'cards' || value === 'rows') view.value = value;
}
</script>

<template>
  <ToggleGroup variant="segmented" aria-label="View" :model-value="view" class="shrink-0" @update:model-value="choose">
    <ToggleInput value="cards" size="sm">
      <ConsoleIcon name="cards" :size="14" />
      <span class="ml-1">Cards</span>
    </ToggleInput>
    <ToggleInput value="rows" size="sm">
      <ConsoleIcon name="rows" :size="14" />
      <span class="ml-1">Rows</span>
    </ToggleInput>
  </ToggleGroup>
</template>
