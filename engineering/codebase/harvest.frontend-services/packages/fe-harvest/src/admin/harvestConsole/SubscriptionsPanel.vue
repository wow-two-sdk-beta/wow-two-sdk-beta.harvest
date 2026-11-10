<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import {
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
  TextInput,
} from '@wow-two-beta/fe/presentation/forms';
import type { DeliveryEvent, SubscriptionModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import { formatDate, stateVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/**
 * Who receives the open container's posts: each tenant subscription to a pipeline, its next delivery page on request,
 * and a form to subscribe a tenant.
 */
defineOptions({ name: 'SubscriptionsPanel' });
const {
  subscriptions,
  pipelines,
  tenants,
  container,
  containerId,
  pending,
  can,
  pipelineOf,
  pipelineLabel,
  pull,
  subscribe,
} = useHarvestConsole();

const subscriptionId = ref('');
const cursor = ref('');
const events = shallowRef<readonly DeliveryEvent[]>([]);
const tenantId = ref('');
const pipelineId = ref('');
const tag = ref('');

const pipelineOptions = computed(() =>
  pipelines.value.map((item) => ({ itemKey: item.id, value: item.id, label: pipelineLabel(item) })),
);
const tenantOptions = computed(() =>
  tenants.value.map((item) => ({ itemKey: item.id, value: item.id, label: item.name })),
);

function tenantName(id: string) {
  return tenants.value.find((item) => item.id === id)?.name ?? id.slice(0, 8);
}

/* The open container decides the default tenant and pipeline; a new container forgets the last delivery page. */
watch(
  [containerId, tenants, pipelines],
  () => {
    if (!tenants.value.some((item) => item.id === tenantId.value))
      tenantId.value = container.value?.tenantId ?? tenants.value[0]?.id ?? '';
    if (!pipelines.value.some((item) => item.id === pipelineId.value)) pipelineId.value = pipelines.value[0]?.id ?? '';
  },
  { immediate: true },
);
watch(containerId, () => {
  subscriptionId.value = '';
  cursor.value = '';
  events.value = [];
});

async function next(subscription: SubscriptionModel) {
  const previous = subscriptionId.value === subscription.id ? cursor.value : '';
  const page = await pull(subscription.id, previous);
  if (!page) return;
  subscriptionId.value = subscription.id;
  cursor.value = page.next;
  events.value = page.data;
}

async function create() {
  const done = await subscribe({ tenantId: tenantId.value, pipelineId: pipelineId.value, tag: tag.value });
  if (done) tag.value = '';
}
</script>

<template>
  <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
    <Card class="self-start p-6">
      <ul v-if="subscriptions.length" class="divide-y divide-border" aria-label="Subscriptions">
        <li
          v-for="subscription in subscriptions"
          :key="subscription.id"
          class="flex flex-wrap items-center justify-between gap-4 py-4 first:pt-0 last:pb-0"
        >
          <div class="min-w-0">
            <p class="font-medium">
              <ConsoleLink
                v-if="subscription.pipelineId && pipelineOf(subscription.pipelineId)"
                :to="{ kind: 'pipeline', id: subscription.pipelineId }"
                class="hover:underline"
              >
                {{ pipelineLabel(pipelineOf(subscription.pipelineId)!) }}
              </ConsoleLink>
              <span v-else>Pipeline subscription</span>
            </p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ tenantName(subscription.tenantId) }} · {{ subscription.schema ?? 'Post' }} · {{ subscription.mode }}
              <span v-if="subscription.filterKey">· {{ subscription.filterKey }}</span>
            </p>
          </div>
          <div class="flex items-center gap-3">
            <Badge :variant="stateVariant(subscription.state)">{{ subscription.state }}</Badge>
            <Button
              v-if="can('events:read')"
              size="sm"
              variant="outline"
              :is-disabled="pending"
              @click="next(subscription)"
            >
              Pull next events
            </Button>
          </div>
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">No subscriptions for this container.</p>
      <div v-if="subscriptionId" class="mt-6 border-t border-border pt-5">
        <h2 class="text-sm font-semibold">Latest delivery page</h2>
        <ul class="mt-3 space-y-2 text-sm">
          <li v-for="event in events" :key="event.eventId">
            {{ event.type }} · revision {{ event.revision ?? '—' }} · {{ formatDate(event.occurredAtUtc) }}
          </li>
        </ul>
        <p v-if="!events.length" class="mt-3 text-sm text-muted-foreground">
          No new events. This cursor will resume from the same position.
        </p>
      </div>
    </Card>
    <Card v-if="can('subscriptions:manage')" class="self-start p-6">
      <h2 class="text-base font-semibold">Subscribe a tenant</h2>
      <form class="mt-5 space-y-4" aria-label="Subscribe a tenant" @submit.prevent="create">
        <Field v-if="tenantOptions.length" label="Tenant">
          <SelectPicker v-model="tenantId" :options="tenantOptions">
            <SelectPickerTrigger><SelectPickerValue placeholder="Choose a tenant" /></SelectPickerTrigger>
            <SelectPickerContent>
              <SelectPickerItem v-for="option in tenantOptions" :key="option.itemKey" v-bind="option" />
            </SelectPickerContent>
          </SelectPicker>
        </Field>
        <label v-else class="block space-y-2 text-sm">
          <span>Tenant ID</span>
          <TextInput v-model="tenantId" :is-required="true" />
        </label>
        <Field label="Pipeline">
          <SelectPicker v-model="pipelineId" :options="pipelineOptions">
            <SelectPickerTrigger><SelectPickerValue placeholder="Choose a pipeline" /></SelectPickerTrigger>
            <SelectPickerContent>
              <SelectPickerItem v-for="option in pipelineOptions" :key="option.itemKey" v-bind="option" />
            </SelectPickerContent>
          </SelectPicker>
        </Field>
        <label class="block space-y-2 text-sm">
          <span>Post tag (optional)</span>
          <TextInput v-model="tag" placeholder="Exact tag to receive" maxlength="196" />
        </label>
        <Button type="submit" :is-disabled="!pipelineId || !tenantId" :is-loading="pending">Subscribe</Button>
      </form>
    </Card>
  </div>
</template>
