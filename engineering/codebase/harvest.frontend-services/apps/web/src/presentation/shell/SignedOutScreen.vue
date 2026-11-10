<script setup lang="ts">
import { HarvestConnectPanel } from '@wow-two-beta/fe-harvest/admin';
import scfdLogo from '../console/assets/scfd-logo.png';

/** The screen outside the frame while the console is not connected: sign-in, the key form, or connecting. */
defineOptions({ name: 'SignedOutScreen' });
const props = defineProps<{
  appVersion: string;
  apiVersion?: string;
  signInPath?: string;
  connectionMessage?: string;
}>();
</script>

<template>
  <main class="surface-ambient flex min-h-dvh flex-col items-center justify-center gap-8 p-6">
    <img :src="scfdLogo" alt="SCFD" width="141" height="28" class="h-7 w-auto dark:brightness-0 dark:invert" />
    <HarvestConnectPanel :sign-in-path="props.signInPath" :connection-message="props.connectionMessage" />
    <p class="flex flex-wrap items-center justify-center gap-3 font-mono text-[11px] text-muted-foreground">
      <span aria-label="App version" :title="props.appVersion">app {{ props.appVersion.split('+')[0] }}</span>
      <span v-if="props.apiVersion" aria-label="API version" :title="props.apiVersion">
        api {{ props.apiVersion.split('+')[0] }}
      </span>
    </p>
  </main>
</template>
