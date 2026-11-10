<script setup lang="ts">
import { SourcePanel, type SourceTab } from '@wow-two-beta/fe-harvest/admin';
import { useGalleryView } from '../../../application/preferences/UseGalleryView';

/** Composes the shared source page on one of its tabs; the Posts tab takes the remembered view. */
defineOptions({ name: 'SourcePage' });
const props = defineProps<{ sourceId: string; tab: SourceTab }>();
const view = useGalleryView();
</script>

<template>
  <SourcePanel :source-id="props.sourceId" :tab="props.tab" :view="view" />
</template>
