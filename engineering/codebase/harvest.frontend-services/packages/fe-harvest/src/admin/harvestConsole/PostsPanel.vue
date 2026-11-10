<script setup lang="ts">
import { computed, ref } from 'vue';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import PostDetailsPanel from './PostDetailsPanel.vue';
import { stateVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/** The open container's captured posts; one opens beside the list with its source links and field evidence. */
defineOptions({ name: 'PostsPanel' });
const { posts, pipelines } = useHarvestConsole();
const selectedId = ref('');
/** The open post, re-read from each refresh so new content and evidence show. */
const selected = computed(() => posts.value.find((post) => post.id === selectedId.value));
</script>

<template>
  <div class="grid gap-6 lg:grid-cols-2">
    <Card class="self-start p-6">
      <ul v-if="posts.length" class="-m-2 divide-y divide-border" aria-label="Posts">
        <li v-for="post in posts" :key="post.id" class="py-1">
          <button
            type="button"
            class="flex w-full items-center justify-between gap-3 rounded-lg p-2 text-left focus-visible:outline-2 focus-visible:outline-primary"
            :class="post.id === selectedId ? 'bg-primary-soft' : 'hover:bg-muted'"
            :aria-pressed="post.id === selectedId"
            @click="selectedId = post.id"
          >
            <div class="min-w-0">
              <p class="truncate font-medium">{{ post.base.title ?? 'Untitled post' }}</p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ post.base.kind }} · revision {{ post.revision }} · {{ post.links.length }} source links
              </p>
            </div>
            <Badge :variant="stateVariant(post.state)">{{ post.state }}</Badge>
          </button>
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">Captured posts appear here after a successful run.</p>
    </Card>
    <Card class="self-start p-6">
      <PostDetailsPanel v-if="selected" :post="selected" :pipelines="pipelines" />
      <p v-else class="text-sm text-muted-foreground">Select a post to inspect its source links and field evidence.</p>
    </Card>
  </div>
</template>
