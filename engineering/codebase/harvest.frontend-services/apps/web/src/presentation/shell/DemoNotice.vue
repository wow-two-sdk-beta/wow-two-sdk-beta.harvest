<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';

/** Says the console runs on synthetic data in this tab, and resets it. */
defineOptions({ name: 'DemoNotice' });
const emit = defineEmits<{ reset: [] }>();
</script>

<template>
  <aside
    aria-label="Demo mode"
    class="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-muted/60 px-4 py-2.5 text-sm"
  >
    <p>Demo · synthetic data in this tab · no backend or source connections</p>
    <Button size="sm" variant="ghost" @click="emit('reset')">Reset demo</Button>
  </aside>
</template>
