<script setup lang="ts">
import { computed } from 'vue';
import {
  EmptyState,
  TabsGroup,
  TabsGroupList,
  TabsGroupPanel,
  TabsGroupTab,
} from '@wow-two-beta/fe/presentation/display';
import ConsoleLink from './ConsoleLink.vue';
import SourceActivityTab from './SourceActivityTab.vue';
import SourceHeader from './SourceHeader.vue';
import SourceOverviewTab from './SourceOverviewTab.vue';
import SourcePostsTab from './SourcePostsTab.vue';
import SourceSettingsTab from './SourceSettingsTab.vue';
import { useHarvestConsole, type SourceTab } from './HarvestConsole';
import type { GalleryView } from './SourceWords';

/**
 * One source on its own page, as Wheelhouse opens a product: the header card over four tabs — Overview, Posts,
 * Activity and, for operators, Settings. Each tab is its own place, `/sources/{id}/{tab}`, so a tab change goes
 * through the host app's router.
 */
defineOptions({ name: 'SourcePanel' });
const props = withDefaults(defineProps<{ sourceId: string; tab?: SourceTab; view?: GalleryView }>(), {
  tab: 'overview',
  view: 'rows',
});
const session = useHarvestConsole();
const { overviewState, navigation, canOperate, canWrite } = session;

const source = computed(() => session.overviewOf(props.sourceId));
const canSet = computed(() => canOperate.value && canWrite('pipelines:manage'));
const postCount = computed(() => source.value?.sections.reduce((sum, section) => sum + section.posts, 0) ?? 0);
const runCount = computed(() => {
  const ids = new Set(source.value?.sections.map((section) => section.pipelineId));
  return session.runs.value.filter((run) => ids.has(run.pipelineId)).length;
});
const tabs = computed(() => [
  { value: 'overview' as const, label: 'Overview' },
  { value: 'posts' as const, label: 'Posts', count: postCount.value },
  { value: 'activity' as const, label: 'Activity', count: runCount.value },
  ...(canSet.value ? [{ value: 'settings' as const, label: 'Settings' }] : []),
]);

function choose(value: string) {
  if (value === props.tab) return;
  const tab = value as SourceTab;
  navigation.value?.go({ kind: 'source', id: props.sourceId, ...(tab === 'overview' ? {} : { tab }) });
}
</script>

<template>
  <div v-if="source" class="flex min-w-0 flex-col gap-5">
    <SourceHeader :source="source" />
    <TabsGroup :model-value="props.tab" activation-mode="manual" class="min-w-0 gap-5" @update:model-value="choose">
      <TabsGroupList
        v-bind="{ 'aria-label': 'Source sections' }"
        class="flex w-full max-w-full overflow-x-auto overflow-y-hidden [scrollbar-width:none]"
      >
        <TabsGroupTab v-for="entry in tabs" :key="entry.value" :value="entry.value" class="shrink-0 whitespace-nowrap">
          {{ entry.label }}
          <span v-if="entry.count !== undefined" class="ms-0.5 text-xs tabular-nums text-muted-foreground">
            {{ entry.count }}
          </span>
        </TabsGroupTab>
      </TabsGroupList>
      <TabsGroupPanel :value="props.tab" class="min-w-0">
        <SourceOverviewTab v-if="props.tab === 'overview'" :source="source" />
        <SourcePostsTab v-else-if="props.tab === 'posts'" :source="source" :view="props.view" />
        <SourceActivityTab v-else-if="props.tab === 'activity'" :source="source" />
        <SourceSettingsTab v-else-if="props.tab === 'settings' && canSet" :source="source" />
        <div v-else class="rounded-2xl border border-border bg-card p-2">
          <EmptyState
            title="Settings are for operators"
            description="An SCFD operator changes how a source is harvested."
          />
        </div>
      </TabsGroupPanel>
    </TabsGroup>
  </div>
  <p v-else-if="overviewState === 'loading'" class="text-sm text-muted-foreground" role="status">Loading the source…</p>
  <div v-else class="rounded-2xl border border-border bg-card p-2">
    <EmptyState title="Source not found" description="This source is not among the sources you can see.">
      <template #actions>
        <ConsoleLink :to="{ kind: 'sources' }" class="text-sm text-primary hover:underline">All sources</ConsoleLink>
      </template>
    </EmptyState>
  </div>
</template>
