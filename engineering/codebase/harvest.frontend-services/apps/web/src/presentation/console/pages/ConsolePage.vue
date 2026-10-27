<script setup lang="ts">
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { ContainersPanel } from '@wow-two-beta/fe-harvest/admin';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';

/**
 * Renders the console: the running versions and the fe-harvest package's management screens (`./admin`), consumed
 * through its exports. Pipelines, runs, hosts and posts join as the console lane lands.
 */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
const { status } = useSystemStatus();
</script>

<template>
  <main class="mx-auto min-h-screen max-w-2xl px-6 py-16 font-sans text-foreground">
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="text-3xl font-semibold">Harvest console</h1>
      <div class="contents" aria-label="App version">
        <Badge>app {{ appVersion }}</Badge>
      </div>
      <div v-if="status" class="contents" aria-label="API version">
        <Badge>api {{ status.version }}</Badge>
      </div>
    </header>

    <section class="mt-8" aria-labelledby="registry-heading">
      <h2 id="registry-heading" class="mb-3 text-sm font-medium text-muted-foreground">Registry</h2>
      <ContainersPanel />
    </section>
  </main>
</template>
