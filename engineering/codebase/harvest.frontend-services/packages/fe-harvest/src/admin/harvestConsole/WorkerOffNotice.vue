<script setup lang="ts">
import { useHarvestConsole } from './HarvestConsole';

/** Explains why queued runs wait when this server runs no capture worker. */
defineOptions({ name: 'WorkerOffNotice' });
const { localWorkerEnabled } = useHarvestConsole();
</script>

<template>
  <div v-if="localWorkerEnabled === false" role="status" class="rounded-xl border border-border bg-card p-4 text-sm">
    <strong>Capture worker is off on this server.</strong>
    Queued runs need an active worker to start.
  </div>
</template>
