<script setup lang="ts">
import { SourcesGallery } from '@wow-two-beta/fe-harvest/admin';
import { useGalleryView } from '../../../application/preferences/UseGalleryView';

/** Composes the shared Sources gallery in the remembered view; `#group-{id}` opens that group. */
defineOptions({ name: 'SourcesPage' });
const props = defineProps<{ focusGroup?: string }>();
const view = useGalleryView();
</script>

<template>
  <SourcesGallery :view="view" :focus-group="props.focusGroup" />
</template>
