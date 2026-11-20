<script setup lang="ts">
import { AvailableSources } from '@wow-two-beta/fe-harvest/admin';
import { useGalleryView } from '../../../application/preferences/UseGalleryView';

/** Composes the shared Available panel in the remembered Cards | Rows view; the frame's bar carries the switch. */
defineOptions({ name: 'AvailablePage' });
const view = useGalleryView();
</script>

<template>
  <AvailableSources :view="view" />
</template>
