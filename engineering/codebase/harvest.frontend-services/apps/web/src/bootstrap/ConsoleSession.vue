<script setup lang="ts">
import { computed } from 'vue';
import { RouterView, useRouter } from 'vue-router';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { HarvestConsoleProvider, type ConsoleNavigation, type HarvestConsole } from '@wow-two-beta/fe-harvest/admin';
import { useDemoConsole } from '../application/demo/UseDemoConsole';
import { useHarvestIdentity } from '../application/identity/UseHarvestIdentity';
import { placeLocation } from '../application/navigation/ConsolePlaces';
import { useSystemStatus } from '../application/system/UseSystemStatus';
import DemoNotice from '../presentation/shell/DemoNotice.vue';
import SignedOutScreen from '../presentation/shell/SignedOutScreen.vue';
import AppLayout, { type ConsoleAccount } from './AppLayout.vue';

/**
 * The console's layout route: binds Identity, the demo client and the service status into one console session, then
 * shows the frame while connected and the signed-out screen, outside the frame, while not.
 */
defineOptions({ name: 'ConsoleSession' });

const appVersion = __APP_VERSION__;
const router = useRouter();
const { demo, session: demoSession, client: demoClient, reset: resetDemo } = useDemoConsole();
const identity = useHarvestIdentity();
const { status, runningVersion } = useSystemStatus(!demo);

/** Lets the shared panels link to this app's routes. */
const navigation: ConsoleNavigation = {
  href: (place) => router.resolve(placeLocation(place)).href,
  go: (place) => void router.push(placeLocation(place)),
};

function localConnectionStorage() {
  if (demo || !['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)) return;
  try {
    return window.sessionStorage;
  } catch {
    return undefined;
  }
}

const connectionStorage = localConnectionStorage();
const signInPath = computed(() =>
  identity.identityEnabled.value && !identity.signedIn.value ? '/api/identity/sign-in?returnUrl=%2F' : undefined,
);

/** Names who holds the session: the demo, the Identity account, or an API key. */
function account(session: HarvestConsole): ConsoleAccount {
  if (demo) return { name: 'Demo operator', description: 'Synthetic data in this tab' };
  if (identity.identityEnabled.value)
    return {
      name: identity.accountName.value,
      description: identity.isOperator.value ? 'SCFD operator' : 'Subscriber member',
      onSignOut: identity.signOut,
      isBusy: identity.pending.value,
    };
  return { description: 'Connected with an API key', signOutLabel: 'Disconnect', onSignOut: session.disconnect };
}
</script>

<template>
  <main v-if="!demo && !identity.ready.value" class="surface-ambient grid min-h-dvh place-items-center p-8">
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
  <HarvestConsoleProvider
    v-else
    :key="`${demoSession}:${identity.epoch.value}`"
    v-slot="{ session }"
    :client="demo ? demoClient : identity.client.value"
    :connection-managed="demo || identity.identityEnabled.value"
    :connection-storage="identity.identityEnabled.value ? undefined : connectionStorage"
    :permissions="identity.identityEnabled.value ? identity.permissions.value : undefined"
    :is-operator="identity.identityEnabled.value ? identity.isOperator.value : undefined"
    :local-worker-enabled="status?.localWorkerEnabled"
    :navigation="navigation"
  >
    <AppLayout
      v-if="session.connected.value"
      :app-version="appVersion"
      :running-version="runningVersion"
      :api-version="status?.version"
      :account="account(session)"
    >
      <template v-if="demo" #notice><DemoNotice @reset="resetDemo" /></template>
      <RouterView />
    </AppLayout>
    <SignedOutScreen
      v-else
      :app-version="appVersion"
      :api-version="status?.version"
      :sign-in-path="signInPath"
      :connection-message="identity.message.value ?? undefined"
    />
  </HarvestConsoleProvider>
</template>
