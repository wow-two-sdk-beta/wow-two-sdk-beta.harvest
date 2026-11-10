<script setup lang="ts">
import { PipelinesPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared pipelines panel; `?source=` opens the new-pipeline form with that source chosen. */
defineOptions({ name: 'PipelinesPage' });
const props = defineProps<{ newSource?: string }>();
</script>

<template>
  <PipelinesPanel :new-source="props.newSource" />
</template>
