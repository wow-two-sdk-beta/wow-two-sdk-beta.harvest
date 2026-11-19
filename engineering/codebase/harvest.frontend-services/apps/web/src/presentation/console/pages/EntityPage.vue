<script setup lang="ts">
import { EntityPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared entity page for one entity, at `/entities/{id}`. */
defineOptions({ name: 'EntityPage' });
const props = defineProps<{ entityId: string }>();
</script>

<template>
  <EntityPanel :entity-id="props.entityId" />
</template>
