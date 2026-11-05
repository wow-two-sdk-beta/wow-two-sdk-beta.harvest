<script setup lang="ts">
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { RouterView } from 'vue-router';

defineOptions({ name: 'AppRoot' });
</script>

<template>
  <RouteAnnouncer />
  <RouterView />
</template>
