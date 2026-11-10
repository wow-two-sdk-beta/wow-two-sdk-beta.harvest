<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink, useRoute } from 'vue-router';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import ConsoleIcon from './ConsoleIcon.vue';
import { ConsoleSections, destinationOf } from './ConsoleDestinations';

/**
 * The sidebar: SCFD's flow from source to subscriber — Overview, Collect, Content, Deliver, System — full or as an
 * icon rail, where labels stay for assistive technology and as tooltips.
 */
defineOptions({ name: 'ConsoleNav' });
const props = withDefaults(defineProps<{ collapsed?: boolean }>(), { collapsed: false });
const emit = defineEmits<{ navigate: [] }>();
const route = useRoute();
const current = computed(() => destinationOf(String(route.name ?? ''))?.destination.route);
const labelClass = computed(() => (props.collapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'));
</script>

<template>
  <SidebarMenu
    v-bind="{ 'aria-label': 'Console' }"
    :is-collapsed="props.collapsed"
    class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden [scrollbar-width:thin]"
  >
    <SidebarMenuSection v-for="section in ConsoleSections" :key="section.label" :label="section.label">
      <SidebarMenuItem
        v-for="destination in section.destinations"
        :key="destination.route"
        as-child
        :is-active="current === destination.route"
      >
        <template #icon><ConsoleIcon :name="destination.icon" /></template>
        <RouterLink
          :to="{ name: destination.route }"
          :title="props.collapsed ? destination.label : undefined"
          @click="emit('navigate')"
        >
          <span :class="labelClass">{{ destination.label }}</span>
        </RouterLink>
      </SidebarMenuItem>
    </SidebarMenuSection>
  </SidebarMenu>
</template>
