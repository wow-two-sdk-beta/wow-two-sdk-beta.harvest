<script lang="ts">
/** Each permission a session can hold, in the words an operator reads, in the order the server documents them. */
export const PermissionWords: readonly { readonly scope: string; readonly words: string }[] = [
  { scope: 'pipelines:read', words: 'See sources, their sections and harvests' },
  { scope: 'pipelines:run', words: 'Start and cancel harvests' },
  { scope: 'pipelines:manage', words: 'Change which sections are harvested and how often' },
  { scope: 'posts:read', words: 'Read harvested posts' },
  { scope: 'sources:manage', words: 'Name sources and file them under groups' },
  { scope: 'subscriptions:manage', words: 'Subscribe a subscriber to a section' },
  { scope: 'events:read', words: 'Pull what a subscription delivers' },
  { scope: 'tenants:manage', words: 'Register subscribers' },
  { scope: 'containers:manage', words: 'Create workspaces' },
];
</script>

<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant, Card } from '@wow-two-beta/fe/presentation/display';
import { useHarvestConsole } from './HarvestConsole';

/**
 * Who may do what: this session's permissions in words, and the workspaces it sees — the shared pool every subscriber
 * can follow, or a subscriber's private one.
 */
defineOptions({ name: 'AccessPanel' });
const { containers, containerId, tenants, pending, can, isOperator, createPool } = useHarvestConsole();
const allowed = computed(() => PermissionWords.filter((item) => can(item.scope)));
const denied = computed(() => PermissionWords.filter((item) => !can(item.scope)));

function workspaceName(item: (typeof containers.value)[number]) {
  if (item.kind === 'pool') return item.externalRef ?? 'Shared pool';
  return (
    item.externalRef ?? `Private · ${tenants.value.find((tenant) => tenant.id === item.tenantId)?.name ?? 'subscriber'}`
  );
}
</script>

<template>
  <div class="grid gap-6 xl:grid-cols-2">
    <Card class="self-start p-6" v-bind="{ 'aria-label': 'This session' }">
      <h2 class="text-base font-semibold">This session</h2>
      <p class="mt-1 text-sm text-muted-foreground">
        {{
          isOperator === undefined
            ? 'The server checks every action against what this connection may do.'
            : isOperator
              ? 'You operate SCFD: every workspace is yours to change.'
              : 'You belong to a subscriber: you change only its private workspace.'
        }}
      </p>
      <h3 class="mt-5 text-sm font-semibold">You can</h3>
      <ul class="mt-2 space-y-1.5 text-sm" aria-label="Allowed">
        <li v-for="item in allowed" :key="item.scope" class="flex items-start gap-2">
          <svg width="16" height="16" viewBox="0 0 24 24" class="mt-0.5 shrink-0 text-success" aria-hidden="true">
            <path
              d="M5 12.5l4.5 4.5L19 7.5"
              fill="none"
              stroke="currentColor"
              stroke-width="2.2"
              stroke-linecap="round"
            />
          </svg>
          {{ item.words }}
        </li>
        <li v-if="!allowed.length" class="text-muted-foreground">Nothing yet.</li>
      </ul>
      <template v-if="denied.length">
        <h3 class="mt-5 text-sm font-semibold">You cannot</h3>
        <ul class="mt-2 space-y-1.5 text-sm text-muted-foreground" aria-label="Not allowed">
          <li v-for="item in denied" :key="item.scope" class="flex items-start gap-2">
            <svg width="16" height="16" viewBox="0 0 24 24" class="mt-0.5 shrink-0" aria-hidden="true">
              <path
                d="M7 7l10 10M17 7 7 17"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
              />
            </svg>
            {{ item.words }}
          </li>
        </ul>
      </template>
    </Card>

    <Card class="self-start p-6" v-bind="{ 'aria-label': 'Workspaces' }">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="text-base font-semibold">Workspaces</h2>
        <Button
          v-if="can('tenants:manage') && can('containers:manage') && !containers.some((item) => item.kind === 'pool')"
          size="sm"
          variant="outline"
          :is-loading="pending"
          @click="createPool"
        >
          Create shared pool
        </Button>
      </div>
      <p class="mt-1 text-sm text-muted-foreground">
        A workspace holds sources and their posts: the shared pool every subscriber can follow, or one subscriber's
        private workspace.
      </p>
      <ul v-if="containers.length" class="mt-4 divide-y divide-border" aria-label="Workspace list">
        <li v-for="item in containers" :key="item.id" class="flex flex-wrap items-center justify-between gap-3 py-3">
          <div class="min-w-0">
            <p class="truncate font-medium">{{ workspaceName(item) }}</p>
            <p class="mt-1 text-xs text-muted-foreground">{{ item.kind === 'pool' ? 'Shared pool' : 'Private' }}</p>
          </div>
          <Badge v-if="item.id === containerId" :variant="BadgeVariant.Brand">open</Badge>
        </li>
      </ul>
      <p v-else class="mt-4 text-sm text-muted-foreground">No workspaces yet.</p>
    </Card>
  </div>
</template>
