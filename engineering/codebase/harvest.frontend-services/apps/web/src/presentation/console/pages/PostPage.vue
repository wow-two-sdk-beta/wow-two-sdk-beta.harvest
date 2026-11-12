<script setup lang="ts">
import { PostPanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared post page for one post of one source, at `/sources/{id}/posts/{postId}`. */
defineOptions({ name: 'PostPage' });
const props = defineProps<{ sourceId: string; postId: string }>();
</script>

<template>
  <PostPanel :source-id="props.sourceId" :post-id="props.postId" />
</template>
