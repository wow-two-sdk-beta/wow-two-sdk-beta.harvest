<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';
import { useDemoConsole } from '../../../application/demo/UseDemoConsole';
import { useHarvestIdentity } from '../../../application/identity/UseHarvestIdentity';
import scfdLogo from '../assets/scfd-logo.png';

/** Composes the reusable console shell with the running app and API versions. */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
const { demo, session: demoSession, client: demoClient, reset: resetDemo } = useDemoConsole();
const identity = useHarvestIdentity();
const { status } = useSystemStatus(!demo);

function localConnectionStorage() {
  if (demo || !['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)) return;
  try {
    return window.sessionStorage;
  } catch {
    return undefined;
  }
}

const connectionStorage = localConnectionStorage();
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
  <main v-if="!demo && !identity.ready.value" class="surface-ambient grid min-h-screen place-items-center p-8">
    <div class="max-w-md space-y-4 rounded-2xl border border-border bg-card p-8">
      <h1 class="text-2xl font-semibold">SCFD</h1>
      <p :role="identity.message.value ? 'alert' : 'status'">
        {{ identity.message.value ?? 'Loading your workspace…' }}
      </p>
      <Button v-if="identity.message.value" :is-loading="identity.pending.value" @click="identity.load">
        Try again
      </Button>
    </div>
  </main>
  <HarvestConsolePanel
    v-else
    :key="`${demoSession}:${identity.epoch.value}`"
    :client="demo ? demoClient : identity.client.value"
    :connection-managed="demo || identity.identityEnabled.value"
    :connection-storage="identity.identityEnabled.value ? undefined : connectionStorage"
    :sign-in-path="
      identity.identityEnabled.value && !identity.signedIn.value ? '/api/identity/sign-in?returnUrl=%2F' : undefined
    "
    :connection-message="identity.message.value ?? undefined"
    :permissions="identity.identityEnabled.value ? identity.permissions.value : undefined"
    :is-operator="identity.identityEnabled.value ? identity.isOperator.value : undefined"
    :app-version="appVersion"
    :api-version="status?.version"
    :local-worker-enabled="status?.localWorkerEnabled"
  >
    <template #brand>
      <img :src="scfdLogo" alt="SCFD" width="141" height="28" class="h-7 w-auto dark:brightness-0 dark:invert" />
    </template>
    <template v-if="identity.identityEnabled.value && identity.signedIn.value" #account>
      <span class="hidden text-sm text-muted-foreground sm:inline">
        {{ identity.accountName.value }}{{ identity.isOperator.value ? ' · SCFD admin' : '' }}
      </span>
      <Button size="sm" variant="outline" :is-loading="identity.pending.value" @click="identity.signOut">
        Sign out
      </Button>
    </template>
  </HarvestConsolePanel>
</template>
