<script lang="ts">
/** Who is signed in, as the account menu shows it. */
export interface ConsoleAccount {
  readonly name?: string;
  readonly description?: string;
  /** Ends the session; omitted, the menu has no sign-out row. */
  readonly onSignOut?: () => unknown;
  /** The sign-out row's label — `Disconnect` for an API key. */
  readonly signOutLabel?: string;
  readonly isBusy?: boolean;
}

/** The sidebar's widths: full, and the icon rail. @internal */
const SidebarWidth = { Full: '248px', Rail: '64px' } as const;
</script>

<script setup lang="ts">
import { computed, useTemplateRef, watch, ref } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { TailwindBreakpoints, useBreakpointBelow } from '@wow-two-beta/fe/foundation/device';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import {
  AppShell,
  AppShellContent,
  AppShellHeader,
  AppShellMain,
  AppShellSidebar,
} from '@wow-two-beta/fe/presentation/layout';
import { AccountMenu, Breadcrumb } from '@wow-two-beta/fe/presentation/nav';
import { RoutePageHeader } from '@wow-two-beta/fe/router';
import { ConsoleNotices, ContainerSwitch, useHarvestConsole } from '@wow-two-beta/fe-harvest/admin';
import { useSidebarCollapsed } from '../application/preferences/UseSidebarCollapsed';
import ConsoleIcon from '../presentation/shell/ConsoleIcon.vue';
import ConsoleNav from '../presentation/shell/ConsoleNav.vue';
import { destinationOf } from '../presentation/shell/ConsoleDestinations';
import scfdLogo from '../presentation/console/assets/scfd-logo.png';

/**
 * The console frame on the fe sdk shell. A 56px top bar: the logo and version above the sidebar, the sidebar toggle,
 * the trail to the current page, the container switch and the account menu. The sidebar lists SCFD's flow and
 * collapses to an icon rail, remembered across visits; below `md` it opens as a drawer. Hairlines divide the regions.
 */
defineOptions({ name: 'AppLayout' });
const props = defineProps<{
  appVersion: string;
  apiVersion?: string;
  account: ConsoleAccount;
}>();
defineSlots<{ default(): unknown; notice?(): unknown }>();
const route = useRoute();
const router = useRouter();
const session = useHarvestConsole();
const collapsed = useSidebarCollapsed();
const isPhone = useBreakpointBelow(TailwindBreakpoints, 'md');
const drawerOpen = ref(false);
const main = useTemplateRef<{ el: HTMLElement | null }>('main');

/** A numbered build shows as `v0.3.4`; a local build shows `dev`. */
const versionLabel = computed(() => {
  const version = props.appVersion.split('+')[0] ?? props.appVersion;
  return /^\d/.test(version) ? `v${version}` : version;
});
/** The sidebar shows as the icon rail by choice, from `md` up. */
const isRail = computed(() => !isPhone.value && collapsed.value);
const sidebarWidth = computed(() => (isRail.value ? SidebarWidth.Rail : SidebarWidth.Full));

const routeName = computed(() => String(route.name ?? ''));
const pipeline = computed(() =>
  routeName.value === 'pipeline' ? session.pipelineOf(String(route.params['id'] ?? '')) : undefined,
);
/** The page's heading: a pipeline's own name on its page, else the route's title. */
const title = computed(() =>
  routeName.value === 'pipeline' ? (pipeline.value ? session.pipelineLabel(pipeline.value) : 'Pipeline') : undefined,
);
/** Section › page › item, the last one the current page. */
const crumbs = computed(() => {
  const place = destinationOf(routeName.value);
  if (!place) return [];
  const page = place.destination;
  const inner = page.route !== routeName.value;
  return [
    { label: place.section.label },
    { label: page.label, ...(inner ? { href: router.resolve({ name: page.route }).href } : {}) },
    ...(inner ? [{ label: title.value ?? '' }] : []),
  ];
});
const versions = computed(() => {
  const [api, commit] = (props.apiVersion ?? '').split('+');
  return [
    { label: 'Console', version: props.appVersion.split('+')[0] ?? props.appVersion },
    { label: 'API', version: api || '—', ...(commit ? { commit } : {}) },
  ];
});

/** Desktop and tablet: collapse or expand the sidebar. Phone: open it as a drawer. @internal */
function toggleSidebar() {
  if (isPhone.value) drawerOpen.value = true;
  else collapsed.value = !collapsed.value;
}

/** Opens a trail link through the router; a modified click keeps the browser's own behaviour. @internal */
function follow(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
    return;
  const href = (event.target as Element | null)?.closest('a')?.getAttribute('href');
  if (!href?.startsWith('/')) return;
  event.preventDefault();
  void router.push(href);
}

/* The main region scrolls, so a new page starts at its top, with the last page's outcome cleared. */
watch(
  () => route.path,
  () => {
    main.value?.el?.scrollTo({ top: 0 });
    drawerOpen.value = false;
    session.clearMessages();
  },
);
</script>

<template>
  <AppShell
    v-model:sidebar-open="drawerOpen"
    class="surface-ambient font-sans text-foreground transition-[grid-template-columns] duration-200 ease-out motion-reduce:transition-none"
    sidebar-breakpoint="md"
    :sidebar-width="sidebarWidth"
  >
    <AppShellHeader height="auto" variant="glass" class="z-20 border-border p-0 pt-[env(safe-area-inset-top)]">
      <div class="flex w-full min-w-0 items-stretch">
        <!-- From tablets up, the logo sits above the sidebar, as wide as it, divided by a hairline. -->
        <div
          class="hidden shrink-0 items-center gap-3 overflow-hidden border-r border-border transition-[width] duration-200 ease-out motion-reduce:transition-none md:flex"
          :class="isRail ? 'justify-center' : 'px-4'"
          :style="{ width: sidebarWidth }"
        >
          <RouterLink :to="{ name: 'home' }" class="flex items-center" aria-label="SCFD home">
            <img v-if="isRail" src="/favicon.png" alt="" width="28" height="28" class="size-7 rounded-md" />
            <img
              v-else
              :src="scfdLogo"
              alt="SCFD"
              width="121"
              height="24"
              class="h-6 w-auto dark:brightness-0 dark:invert"
            />
          </RouterLink>
          <span
            v-if="!isRail"
            aria-label="App version"
            class="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[11px] font-medium text-muted-foreground"
            :title="`Console ${props.appVersion}`"
          >
            {{ versionLabel }}
          </span>
        </div>
        <div
          class="flex min-h-14 min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2 md:flex-nowrap md:py-0 lg:px-6"
        >
          <RouterLink :to="{ name: 'home' }" class="shrink-0 md:hidden" aria-label="SCFD home">
            <img src="/favicon.png" alt="" width="26" height="26" class="size-6.5 rounded-md" />
          </RouterLink>
          <Button
            variant="ghost"
            tone="neutral"
            size="md"
            shape="square"
            class="shrink-0"
            :aria-label="isPhone ? 'Open navigation' : collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
            :aria-expanded="isPhone ? drawerOpen : !collapsed"
            :title="isPhone ? 'Open navigation' : collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
            @click="toggleSidebar"
          >
            <ConsoleIcon name="panel" :size="18" />
          </Button>
          <Breadcrumb :items="crumbs" class="min-w-0 flex-1 truncate text-sm" @click="follow" />
          <ContainerSwitch class="shrink-0" />
          <AccountMenu
            class="shrink-0"
            :name="props.account.name"
            :description="props.account.description"
            :versions="versions"
            :is-busy="props.account.isBusy"
            :sign-out-label="props.account.signOutLabel"
            :on-sign-out="props.account.onSignOut"
          />
        </div>
      </div>
    </AppShellHeader>
    <AppShellSidebar class="border-border bg-transparent">
      <ConsoleNav :collapsed="isRail" @navigate="drawerOpen = false" />
    </AppShellSidebar>
    <AppShellMain ref="main">
      <AppShellContent gutter="lg" max-width="2xl" class="lg:px-6 xl:px-8">
        <div class="space-y-6 pb-10">
          <slot name="notice"></slot>
          <RoutePageHeader :title="title" size="lg" />
          <ConsoleNotices />
          <slot></slot>
        </div>
      </AppShellContent>
    </AppShellMain>
  </AppShell>
</template>
