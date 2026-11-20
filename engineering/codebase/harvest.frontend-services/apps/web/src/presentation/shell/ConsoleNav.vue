<script setup lang="ts">
import { computed, shallowRef, watchEffect } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import { OtherGroupKey, SourceIcon, groupElementId, useHarvestConsole } from '@wow-two-beta/fe-harvest/admin';
import type { PipelineStageKind } from '@wow-two-beta/fe-harvest/contracts';
import ConsoleIcon from './ConsoleIcon.vue';
import { ConsoleSections, destinationOf, isPipelineRoute, isSourceRoute } from './ConsoleDestinations';

/**
 * The sidebar: SCFD's flow from source to subscriber — Overview, Collect, Deliver, System — full or as an icon rail,
 * where labels stay for assistive technology and as tooltips. Keeps links to the last source and pipeline in the workspace.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<{ collapsed?: boolean }>(), { collapsed: false });
const route = useRoute();
const session = useHarvestConsole();
const routeName = computed(() => String(route.name ?? ''));
const inSource = computed(() => isSourceRoute(routeName.value));
const inPipeline = computed(() => isPipelineRoute(routeName.value));
const labelClass = computed(() => (props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'));

const focusedSource = shallowRef<{ containerId: string; id: string }>();
const focusedStage = shallowRef<{ pipelineId: string; kind: PipelineStageKind }>();
watchEffect(() => {
  const containerId = session.containerId.value;
  const id = String(route.params['id'] ?? '');
  if (inPipeline.value) {
    const pipeline = session.pipelineOf(id);
    if (pipeline && pipeline.containerId === containerId) {
      focusedSource.value = { containerId, id: pipeline.sourceId };
      session.focusedPipelineId.value = id;
      const stage = route.params['stage'];
      focusedStage.value =
        stage === 'discover' || stage === 'harvest' || stage === 'process'
          ? { pipelineId: id, kind: stage }
          : undefined;
    }
  } else if (inSource.value && session.overviewOf(id)) {
    focusedSource.value = { containerId, id };
  }
});

const trail = computed(() => {
  if (focusedSource.value?.containerId !== session.containerId.value) return undefined;
  const id = focusedSource.value.id;
  const source = session.overviewOf(id);
  if (!source) return undefined;
  const group = session.groupOf(source.groupId);
  const focused = session.pipelineOf(session.focusedPipelineId.value);
  const pipelineId = focused?.sourceId === id ? focused.id : source.sections[0]?.pipelineId;
  const stage = focusedStage.value?.pipelineId === pipelineId ? focusedStage.value?.kind : undefined;
  const section = pipelineId ? session.sectionLabel(pipelineId) : '';
  const stageLabel = stage ? stage.charAt(0).toUpperCase() + stage.slice(1) : undefined;
  return {
    id,
    source,
    group: { key: group?.id ?? OtherGroupKey, name: group?.name ?? 'Other' },
    pipeline:
      pipelineId && session.can('pipelines:read')
        ? {
            id: pipelineId,
            label: stageLabel ? `${stageLabel} pipeline` : section,
            action: stageLabel ? `Open ${stageLabel} pipeline for ${section}` : `Open ${section} pipelines`,
            stage,
          }
        : undefined,
  };
});
const activeDestination = computed(() => {
  const current = destinationOf(routeName.value)?.destination.route;
  if (current === 'sources' && trail.value && inSource.value) return undefined;
  if (current === 'pipelines' && trail.value?.pipeline && inPipeline.value) return undefined;
  return current;
});
/** Each destination's count beside its label: sources, the sources still to turn on, subscribers. */
const counts = computed<Record<string, number | undefined>>(() => ({
  sources: session.overviews.value.length || undefined,
  available: session.availableCount.value || undefined,
  subscribers: session.tenants.value.length || undefined,
}));
/** The destinations this session sees: Available is the operator's. */
const sections = computed(() =>
  ConsoleSections.map((section) => ({
    ...section,
    destinations: section.destinations.filter((destination) => !destination.operatorOnly || session.canOperate.value),
  })),
);
</script>

<template>
  <SidebarMenu
    v-bind="{ 'aria-label': 'Console' }"
    :is-collapsed="props.collapsed"
    class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden [scrollbar-width:thin]"
  >
    <SidebarMenuSection v-for="section in sections" :key="section.label" :label="section.label">
      <template v-for="destination in section.destinations" :key="destination.route">
        <Tooltip :content="destination.label" :is-disabled="!props.collapsed" :open-delay="150" placement="right">
          <SidebarMenuItem as-child :is-active="activeDestination === destination.route">
            <template #icon><ConsoleIcon :name="destination.icon" /></template>
            <template v-if="counts[destination.route]" #trailing>
              <span class="text-xs tabular-nums text-muted-foreground">{{ counts[destination.route] }}</span>
            </template>
            <RouterLink :to="{ name: destination.route }">
              <span :class="labelClass">{{ destination.label }}</span>
            </RouterLink>
          </SidebarMenuItem>
        </Tooltip>
        <li v-if="destination.route === 'sources' && trail" class="flex flex-col">
          <ul
            aria-label="Open source"
            class="flex flex-col gap-0.5"
            :class="props.collapsed ? 'rounded-lg bg-muted/70 py-0.5' : 'ms-4 border-s border-border ps-2 pt-0.5'"
          >
            <Tooltip :content="`${trail.group.name}: all its sources`" :open-delay="150" placement="right">
              <SidebarMenuItem as-child>
                <template #icon><ConsoleIcon name="group" :size="15" /></template>
                <RouterLink
                  :to="{ name: 'sources', hash: `#${groupElementId(trail.group.key)}` }"
                  class="h-8 text-[13px]"
                >
                  <span :class="labelClass">{{ trail.group.name }}</span>
                </RouterLink>
              </SidebarMenuItem>
            </Tooltip>
            <li class="flex flex-col">
              <ul :class="props.collapsed ? '' : 'ms-3 border-s border-border ps-2'">
                <Tooltip
                  :content="trail.source.name"
                  :is-disabled="!props.collapsed"
                  :open-delay="150"
                  placement="right"
                >
                  <SidebarMenuItem as-child :is-active="inSource && route.params['id'] === trail.id">
                    <template #icon>
                      <SourceIcon :family="trail.source.family" :favicon-url="trail.source.faviconUrl" size="xs" bare />
                    </template>
                    <RouterLink :to="{ name: 'source', params: { id: trail.id } }">
                      <span :class="labelClass">{{ trail.source.name }}</span>
                    </RouterLink>
                  </SidebarMenuItem>
                </Tooltip>
                <Tooltip v-if="trail.pipeline" :content="trail.pipeline.action" :open-delay="150" placement="right">
                  <SidebarMenuItem as-child :is-active="inPipeline && route.params['id'] === trail.pipeline.id">
                    <template #icon><ConsoleIcon name="pipelines" :size="15" /></template>
                    <RouterLink
                      :to="
                        trail.pipeline.stage
                          ? { name: 'pipeline-stage', params: { id: trail.pipeline.id, stage: trail.pipeline.stage } }
                          : { name: 'pipeline', params: { id: trail.pipeline.id } }
                      "
                      :aria-label="trail.pipeline.action"
                      class="mt-1 h-8 border border-border text-[13px]"
                    >
                      <span :class="labelClass">{{ trail.pipeline.label }}</span>
                    </RouterLink>
                  </SidebarMenuItem>
                </Tooltip>
              </ul>
            </li>
          </ul>
        </li>
      </template>
    </SidebarMenuSection>
  </SidebarMenu>
</template>
