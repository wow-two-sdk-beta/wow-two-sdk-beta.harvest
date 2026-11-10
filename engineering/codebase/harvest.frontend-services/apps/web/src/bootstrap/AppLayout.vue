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

/** The tabs of a source's page, as the trail names them. @internal */
const SourceTabLabels: Readonly<Record<string, string>> = {
  'source-posts': 'Posts',
  'source-activity': 'Activity',
  'source-settings': 'Settings',
};
</script>

<script setup lang="ts">
import { computed, useTemplateRef, watch } from 'vue';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { AppFrame } from '@wow-two-beta/fe/presentation/layout';
import { RoutePageHeader } from '@wow-two-beta/fe/router';
import {
  ConsoleNotices,
  ContainerSwitch,
  OtherGroupKey,
  groupElementId,
  useHarvestConsole,
} from '@wow-two-beta/fe-harvest/admin';
import ConsoleNav from '../presentation/shell/ConsoleNav.vue';
import GalleryViewSwitch from '../presentation/shell/GalleryViewSwitch.vue';
import { destinationOf, isSourceRoute } from '../presentation/shell/ConsoleDestinations';
import scfdLogo from '../presentation/console/assets/scfd-logo.png';

/**
 * The console frame on the fe sdk `AppFrame`: the logo and version above the sidebar, the sidebar toggle, the trail to
 * the current page, the Cards | Rows switch on the galleries, the workspace switch and the account menu. The sidebar
 * lists SCFD's flow and collapses to an icon rail, remembered under `scfd.sidebar-collapsed`.
 */
defineOptions({ name: 'AppLayout' });
const props = defineProps<{
  appVersion: string;
  /** The release the host serves now; the version badge offers a reload while the tab runs another. */
  runningVersion?: string | null;
  apiVersion?: string;
  account: ConsoleAccount;
}>();
defineSlots<{ default(): unknown; notice?(): unknown }>();
const route = useRoute();
const router = useRouter();
const session = useHarvestConsole();
const frame = useTemplateRef<{ scrollToTop(): void }>('frame');

const routeName = computed(() => String(route.name ?? ''));
const isSource = computed(() => isSourceRoute(routeName.value));
/** The galleries carry the Cards | Rows switch: Sources and a source's Posts. */
const hasViewSwitch = computed(() => routeName.value === 'sources' || routeName.value === 'source-posts');

/** Section › page › group › source › tab, the last one the current page. */
const crumbs = computed(() => {
  const place = destinationOf(routeName.value);
  if (!place) return [];
  const page = place.destination;
  const head = [{ label: place.section.label }];
  if (!isSource.value) return [...head, { label: page.label }];
  const id = String(route.params['id'] ?? '');
  const source = session.overviewOf(id);
  const group = source ? session.groupOf(source.groupId) : undefined;
  const groupKey = group?.id ?? OtherGroupKey;
  const tab = SourceTabLabels[routeName.value];
  const sourceHref = router.resolve({ name: 'source', params: { id } }).href;
  return [
    ...head,
    { label: page.label, href: router.resolve({ name: page.route }).href },
    ...(source
      ? [
          {
            label: group?.name ?? 'Other',
            href: router.resolve({ name: 'sources', hash: `#${groupElementId(groupKey)}` }).href,
          },
        ]
      : []),
    tab ? { label: source?.name ?? 'Source', href: sourceHref } : { label: source?.name ?? 'Source' },
    ...(tab ? [{ label: tab }] : []),
  ];
});
const account = computed(() => {
  const [api, commit] = (props.apiVersion ?? '').split('+');
  return {
    ...props.account,
    versions: [
      { label: 'Console', version: props.appVersion.split('+')[0] ?? props.appVersion },
      { label: 'API', version: api || '—', ...(commit ? { commit } : {}) },
    ],
  };
});

/** Opens a trail link through the router. @internal */
function navigate(href: string) {
  void router.push(href);
}

/* A new page starts at its top, with the last page's outcome cleared. */
watch(
  () => route.path,
  () => {
    frame.value?.scrollToTop();
    session.clearMessages();
  },
);
</script>

<template>
  <AppFrame
    ref="frame"
    class="surface-ambient font-sans text-foreground"
    :version="props.appVersion"
    :running-version="props.runningVersion"
    :crumbs="crumbs"
    :account="account"
    collapse-storage-key="scfd.sidebar-collapsed"
    @navigate="navigate"
  >
    <template #brand="{ isRail }">
      <RouterLink :to="{ name: 'home' }" class="flex shrink-0 items-center" aria-label="SCFD home">
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
    </template>
    <template #nav="{ isRail }"><ConsoleNav :collapsed="isRail" /></template>
    <template #bar-end>
      <GalleryViewSwitch v-if="hasViewSwitch" />
      <ContainerSwitch class="shrink-0" />
    </template>
    <template #notice>
      <div v-if="$slots.notice" class="mb-6"><slot name="notice"></slot></div>
    </template>
    <div class="space-y-6 pb-10">
      <RoutePageHeader v-if="!isSource" size="lg" />
      <ConsoleNotices />
      <slot></slot>
    </div>
  </AppFrame>
</template>
