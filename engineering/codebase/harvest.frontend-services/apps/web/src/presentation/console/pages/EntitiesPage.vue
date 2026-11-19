<script setup lang="ts">
import { EntitiesPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared entities panel: search, type and year, or the calendar of the next twelve months. */
defineOptions({ name: 'EntitiesPage' });
</script>

<template>
  <EntitiesPanel />
</template>
