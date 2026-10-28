<script setup lang="ts">
import { HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';
import { useDemoConsole } from '../../../application/demo/UseDemoConsole';

/** Composes the reusable console shell with the running app and API versions. */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
const { demo, session: demoSession, client, reset: resetDemo } = useDemoConsole();
const { status } = useSystemStatus(!demo);
</script>

<template>
  <aside
    v-if="demo"
    class="flex items-center justify-between gap-4 border-b border-border bg-muted px-6 py-3 text-sm"
    aria-label="Demo mode"
  >
    <p>Demo · synthetic data in this tab · no backend or source connections</p>
    <button type="button" class="underline" @click="resetDemo">Reset demo</button>
  </aside>
  <HarvestConsolePanel
    :key="demoSession"
    :client="client"
    :connection-managed="demo"
    :app-version="appVersion"
    :api-version="status?.version"
  />
</template>
