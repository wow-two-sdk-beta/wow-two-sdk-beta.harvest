<script setup lang="ts">
import { useHarvestConsole } from './HarvestConsole';

/** Shows the last action's outcome: a failure as an alert, a success as a status line. */
defineOptions({ name: 'ConsoleNotices' });
const { failure, notice } = useHarvestConsole();
</script>

<template>
  <div v-if="failure || notice" class="space-y-2">
    <p v-if="failure" class="rounded-xl border border-destructive/25 bg-card p-4 text-sm text-destructive" role="alert">
      {{ failure }}
    </p>
    <p v-if="notice" class="text-sm text-primary" role="status">{{ notice }}</p>
  </div>
</template>
