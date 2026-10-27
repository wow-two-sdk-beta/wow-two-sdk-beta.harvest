<script setup lang="ts">
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { Navbar } from '@wow-two-beta/fe/presentation/layout';
import { HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
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
  <main class="surface-ambient min-h-screen font-sans text-foreground">
    <Navbar variant="glass" :is-sticky="true" class="z-10 px-2" container-size="full">
      <div class="mx-auto flex max-w-7xl flex-wrap items-center gap-3">
        <a href="/" class="mr-auto text-xl font-semibold tracking-tight">
          Harvest
          <span class="ml-3 text-xs font-normal uppercase tracking-[0.2em] text-muted-foreground">Console</span>
        </a>
        <div class="contents" aria-label="App version">
          <Badge>app {{ appVersion }}</Badge>
        </div>
        <div v-if="status" class="contents" aria-label="API version">
          <Badge>api {{ status.version }}</Badge>
        </div>
      </div>
    </Navbar>

    <section class="mx-auto max-w-7xl px-4 py-8 sm:px-6 sm:py-12" aria-labelledby="registry-heading">
      <div class="mb-8">
        <p class="text-xs font-semibold uppercase tracking-[0.22em] text-primary">Source operations</p>
        <h1 id="registry-heading" class="mt-3 text-3xl font-semibold tracking-tight">A clear view of every harvest.</h1>
        <p class="mt-3 text-sm text-muted-foreground">Schedule sources. Follow runs. Deliver what changed.</p>
      </div>
      <HarvestConsolePanel />
    </section>
  </main>
</template>
