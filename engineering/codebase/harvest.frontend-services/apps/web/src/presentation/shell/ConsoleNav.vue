<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { OtherGroupKey, SourceIcon, groupElementId, useHarvestConsole } from '@wow-two-beta/fe-harvest/admin';
import ConsoleIcon from './ConsoleIcon.vue';
import { ConsoleSections, destinationOf, isSourceRoute } from './ConsoleDestinations';

/**
 * The sidebar: SCFD's flow from source to subscriber — Overview, Collect, Deliver, System — full or as an icon rail,
 * where labels stay for assistive technology and as tooltips. While a source is open, it shows under its group beneath
 * Sources, as Wheelhouse shows a product under its workspace: the group opens its section of the Sources page, and the
 * source is the current place.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<{ collapsed?: boolean }>(), { collapsed: false });
const route = useRoute();
const session = useHarvestConsole();
const routeName = computed(() => String(route.name ?? ''));
const current = computed(() => destinationOf(routeName.value)?.destination.route);
const labelClass = computed(() => (props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'));

/** The open source and its group, while a source's page is open. */
const trail = computed(() => {
  if (!isSourceRoute(routeName.value)) return undefined;
  const id = String(route.params['id'] ?? '');
  const source = session.overviewOf(id);
  if (!source) return undefined;
  const group = session.groupOf(source.groupId);
  return { id, source, group: { key: group?.id ?? OtherGroupKey, name: group?.name ?? 'Other' } };
});
/** Each destination's count beside its label: sources, subscribers. */
const counts = computed<Record<string, number | undefined>>(() => ({
  sources: session.overviews.value.length || undefined,
  subscribers: session.tenants.value.length || undefined,
}));
</script>

<template>
  <SidebarMenu
    v-bind="{ 'aria-label': 'Console' }"
    :is-collapsed="props.collapsed"
    class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden [scrollbar-width:thin]"
  >
    <SidebarMenuSection v-for="section in ConsoleSections" :key="section.label" :label="section.label">
      <template v-for="destination in section.destinations" :key="destination.route">
        <SidebarMenuItem
          as-child
          :is-active="current === destination.route && !(trail && destination.route === 'sources')"
        >
          <template #icon><ConsoleIcon :name="destination.icon" /></template>
          <template v-if="counts[destination.route]" #trailing>
            <span class="text-xs tabular-nums text-muted-foreground">{{ counts[destination.route] }}</span>
          </template>
          <RouterLink :to="{ name: destination.route }" :title="props.collapsed ? destination.label : undefined">
            <span :class="labelClass">{{ destination.label }}</span>
          </RouterLink>
        </SidebarMenuItem>
        <li v-if="destination.route === 'sources' && trail" class="flex flex-col">
          <ul
            aria-label="Open source"
            class="flex flex-col gap-0.5"
            :class="props.collapsed ? 'rounded-lg bg-muted/70 py-0.5' : 'ms-4 border-s border-border ps-2 pt-0.5'"
          >
            <SidebarMenuItem as-child>
              <template #icon><ConsoleIcon name="group" :size="15" /></template>
              <RouterLink
                :to="{ name: 'sources', hash: `#${groupElementId(trail.group.key)}` }"
                :title="`${trail.group.name}: all its sources`"
                class="h-8 text-[13px]"
              >
                <span :class="labelClass">{{ trail.group.name }}</span>
              </RouterLink>
            </SidebarMenuItem>
            <li class="flex flex-col">
              <ul :class="props.collapsed ? '' : 'ms-3 border-s border-border ps-2'">
                <SidebarMenuItem as-child is-active>
                  <template #icon><SourceIcon :family="trail.source.family" size="xs" bare /></template>
                  <RouterLink :to="{ name: 'source', params: { id: trail.id } }" :title="trail.source.name">
                    <span :class="labelClass">{{ trail.source.name }}</span>
                  </RouterLink>
                </SidebarMenuItem>
              </ul>
            </li>
          </ul>
        </li>
      </template>
    </SidebarMenuSection>
  </SidebarMenu>
</template>
