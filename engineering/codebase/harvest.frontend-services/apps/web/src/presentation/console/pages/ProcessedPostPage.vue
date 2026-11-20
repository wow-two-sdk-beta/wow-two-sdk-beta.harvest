<script setup lang="ts">
import { ProcessedPostPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared processed-post page for one post of one source, at `/sources/{id}/processed/{postId}`. */
defineOptions({ name: 'ProcessedPostPage' });
const props = defineProps<{ sourceId: string; postId: string }>();
</script>

<template>
  <ProcessedPostPanel :source-id="props.sourceId" :post-id="props.postId" />
</template>
