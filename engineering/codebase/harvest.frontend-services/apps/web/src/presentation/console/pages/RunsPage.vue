<script setup lang="ts">
import { RunsPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared runs panel; `?pipeline=` narrows it to one pipeline. */
defineOptions({ name: 'RunsPage' });
const props = defineProps<{ pipeline?: string }>();
</script>

<template>
  <RunsPanel :pipeline="props.pipeline" />
</template>
