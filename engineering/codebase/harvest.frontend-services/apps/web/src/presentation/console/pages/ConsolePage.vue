<script setup lang="ts">
import { HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';

/** Composes the reusable console shell with the running app and API versions. */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
const { status } = useSystemStatus();
</script>

<template>
  <HarvestConsolePanel :app-version="appVersion" :api-version="status?.version" />
</template>
