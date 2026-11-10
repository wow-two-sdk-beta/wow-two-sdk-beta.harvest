<script setup lang="ts">
import { AuthProvider } from '@wow-two-beta/fe/auth';
import { ColorModeProvider } from '@wow-two-beta/fe/foundation/primitives';
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { provide } from 'vue';
import { RouterView } from 'vue-router';
import { createHarvestIdentity, HarvestIdentityKey } from '../application/identity/UseHarvestIdentity';
import { ThemeStorageKey } from './theme';

defineOptions({ name: 'AppRoot' });
const demo = import.meta.env.MODE === 'demo';
const identity = createHarvestIdentity(!demo);
provide(HarvestIdentityKey, identity);
</script>

<template>
  <ColorModeProvider :storage-key="ThemeStorageKey">
    <AuthProvider
      :strategy="identity.strategy"
      :bridge="identity.bridge"
      :resolve-on-mount="!demo"
      :on-resolve-error="identity.resolveError"
    >
      <RouteAnnouncer />
      <RouterView />
    </AuthProvider>
  </ColorModeProvider>
</template>
