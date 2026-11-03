<script setup lang="ts">
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { RouterView } from 'vue-router';
import { provide } from 'vue';
import { AuthProvider } from '@wow-two-beta/fe/auth';
import { createHarvestIdentity, HarvestIdentityKey } from '../application/identity/UseHarvestIdentity';

/** Composes the Harvest service's one app: its hosted pages and its console, each a route. */
defineOptions({ name: 'AppRoot' });
const demo = import.meta.env.MODE === 'demo';
const identity = createHarvestIdentity(!demo);
provide(HarvestIdentityKey, identity);
</script>

<template>
  <AuthProvider
    :strategy="identity.strategy"
    :bridge="identity.bridge"
    :resolve-on-mount="!demo"
    :on-resolve-error="identity.resolveError"
  >
    <RouteAnnouncer />
    <RouterView />
  </AuthProvider>
</template>
