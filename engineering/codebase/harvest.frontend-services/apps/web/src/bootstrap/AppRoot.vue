<script setup lang="ts">
import { AuthProvider } from '@wow-two-beta/fe/auth';
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { provide } from 'vue';
import { RouterView } from 'vue-router';
import { createHarvestIdentity, HarvestIdentityKey } from '../application/identity/UseHarvestIdentity';

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
