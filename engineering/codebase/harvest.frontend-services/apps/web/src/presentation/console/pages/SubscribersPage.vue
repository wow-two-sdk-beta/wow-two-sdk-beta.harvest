<script setup lang="ts">
import { SubscribersPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared subscribers panel. */
defineOptions({ name: 'SubscribersPage' });
const props = defineProps<{ section?: string }>();
</script>

<template>
  <SubscribersPanel :section="props.section" />
</template>
