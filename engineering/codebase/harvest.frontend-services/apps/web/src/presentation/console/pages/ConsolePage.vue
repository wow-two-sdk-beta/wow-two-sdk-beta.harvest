<script setup lang="ts">
import { HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';
import { useDemoConsole } from '../../../application/demo/UseDemoConsole';
import { useHarvestIdentity } from '../../../application/identity/UseHarvestIdentity';
import { Button } from '@wow-two-beta/fe/presentation/actions';

/** Composes the reusable console shell with the running app and API versions. */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
const { demo, session: demoSession, client: demoClient, reset: resetDemo } = useDemoConsole();
const identity = useHarvestIdentity();
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
  <main v-if="!demo && !identity.ready.value" class="surface-ambient grid min-h-screen place-items-center p-8">
    <div class="max-w-md space-y-4 rounded-2xl border border-border bg-card p-8">
      <h1 class="text-2xl font-semibold">Harvest</h1>
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
    :sign-in-path="
      identity.identityEnabled.value && !identity.signedIn.value ? '/api/identity/sign-in?returnUrl=%2F' : undefined
    "
    :connection-message="identity.message.value ?? undefined"
    :permissions="identity.identityEnabled.value ? identity.permissions.value : undefined"
    :app-version="appVersion"
    :api-version="status?.version"
  >
    <template v-if="identity.identityEnabled.value && identity.signedIn.value" #account>
      <span class="hidden text-sm text-muted-foreground sm:inline">{{ identity.accountName.value }}</span>
      <Button size="sm" variant="outline" :is-loading="identity.pending.value" @click="identity.signOut">
        Sign out
      </Button>
    </template>
  </HarvestConsolePanel>
</template>
