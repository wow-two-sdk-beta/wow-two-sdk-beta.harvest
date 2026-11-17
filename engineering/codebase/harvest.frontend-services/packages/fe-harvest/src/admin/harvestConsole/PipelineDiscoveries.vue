<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { DiscoveryState, PipelineDiscoveryModel } from '../../contracts/PipelineStagesModel';
import ChoicePicker from './ChoicePicker.vue';
import { useHarvestConsole } from './HarvestConsole';
import ConsoleLink from './ConsoleLink.vue';

const props = defineProps<{ sourceId: string; pipelineId: string }>();
const session = useHarvestConsole();
const client = computed(() => session.client.value?.pipelineStages);
const state = ref<DiscoveryState | 'all'>('all');
const rows = shallowRef<readonly PipelineDiscoveryModel[]>([]);
const loading = ref(false);
const error = ref(false);
const offset = ref(0);
const limit = 50;
const states = [
  { value: 'all', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'captured', label: 'Captured' },
  { value: 'failed', label: 'Failed' },
  { value: 'gone', label: 'Gone' },
] as const;

function filterBy(value: DiscoveryState | 'all') {
  offset.value = 0;
  state.value = value;
}

watch(
  [client, () => props.pipelineId, offset, state],
  ([api, id, page, filter], _, cancel) => {
    rows.value = [];
    loading.value = !!api;
    error.value = false;
    if (!api) return;
    const controller = new AbortController();
    cancel(() => controller.abort());
    void api.discoveries(id, page, limit, filter === 'all' ? undefined : filter, controller.signal).then(
      (result) => {
        if (controller.signal.aborted) return;
        if (result.ok) rows.value = result.value;
        error.value = !result.ok;
        loading.value = false;
      },
      () => {
        if (controller.signal.aborted) return;
        error.value = true;
        loading.value = false;
      },
    );
  },
  { immediate: true },
);
</script>

<template>
  <section aria-label="Discoveries" class="grid min-w-0 gap-3 rounded-2xl border border-border bg-card p-4 sm:p-5">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="font-semibold">Discoveries</h2>
      <div class="flex items-center gap-2 text-sm">
        <span>Status</span>
        <div class="w-32">
          <ChoicePicker :model-value="state" :choices="states" label="Status" @update:model-value="filterBy" />
        </div>
      </div>
    </div>
    <p v-if="error || !client" class="text-sm text-destructive" role="alert">Discoveries could not be loaded.</p>
    <p v-else-if="loading" class="text-sm text-muted-foreground" role="status">Loading discoveries…</p>
    <p v-else-if="!rows.length" class="text-sm text-muted-foreground">No discoveries on this page.</p>
    <div v-else class="overflow-x-auto">
      <table class="w-full text-left text-sm">
        <thead class="text-muted-foreground">
          <tr class="border-b border-border">
            <th scope="col" class="py-2 pr-3">Post</th>
            <th scope="col" class="py-2 pr-3">Catalog</th>
            <th scope="col" class="py-2 pr-3">Last seen</th>
            <th scope="col" class="py-2">Status</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in rows" :key="item.id" class="border-b border-border">
            <td class="max-w-72 py-2 pr-3">
              <ConsoleLink
                v-if="item.postId"
                :to="{ kind: 'source', id: sourceId, post: item.postId }"
                class="break-words text-primary hover:underline"
              >
                {{ item.title || item.externalKey }}
              </ConsoleLink>
              <a
                v-else
                :href="item.url"
                target="_blank"
                rel="noopener noreferrer"
                class="break-words text-primary hover:underline"
              >
                {{ item.title || item.externalKey }}
              </a>
            </td>
            <td class="py-2 pr-3">
              <a :href="item.catalogUrl" target="_blank" rel="noopener noreferrer" class="text-primary hover:underline">
                Page
              </a>
            </td>
            <td class="whitespace-nowrap py-2 pr-3">{{ new Date(item.lastSeenAtUtc).toLocaleString() }}</td>
            <td class="py-2 capitalize">{{ item.state }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <div v-if="offset > 0 || rows.length === limit" class="flex gap-2">
      <Button size="sm" variant="outline" :is-disabled="offset === 0 || loading" @click="offset -= limit">
        Previous
      </Button>
      <Button size="sm" variant="outline" :is-disabled="rows.length < limit || loading" @click="offset += limit">
        Next
      </Button>
    </div>
  </section>
</template>
