<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant, Card } from '@wow-two-beta/fe/presentation/display';
import { TextInput } from '@wow-two-beta/fe/presentation/forms';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole } from './HarvestConsole';
import { dateOf } from './SourceWords';

/**
 * Who receives posts: the subscribers — the products that follow SCFD — and their subscriptions, each naming the
 * source and section it follows. An operator registers a subscriber and subscribes it by choosing a source and one of
 * its sections; nothing here types a URL or a tag.
 */
defineOptions({ name: 'SubscribersPanel' });
const session = useHarvestConsole();
const { tenants, subscriptions, pipelines, pending, can, registerTenant, subscribe } = session;
const name = ref('');
const tenantId = ref('');
const sourceId = ref('');
const pipelineId = ref('');

const subscriberChoices = computed(() => tenants.value.map((item) => ({ value: item.id, label: item.name })));
const sourceChoices = computed(() =>
  [...new Set(pipelines.value.map((item) => item.sourceId))].map((id) => ({
    value: id,
    label: session.sourceName(id),
  })),
);
const sectionChoices = computed(() =>
  pipelines.value
    .filter((item) => item.sourceId === sourceId.value)
    .map((item) => ({ value: item.id, label: session.sectionLabel(item.id) })),
);

watch(
  [tenants, sourceChoices, sectionChoices],
  () => {
    if (!tenants.value.some((item) => item.id === tenantId.value)) tenantId.value = tenants.value[0]?.id ?? '';
    if (!sourceChoices.value.some((item) => item.value === sourceId.value))
      sourceId.value = sourceChoices.value[0]?.value ?? '';
    if (!sectionChoices.value.some((item) => item.value === pipelineId.value))
      pipelineId.value = sectionChoices.value[0]?.value ?? '';
  },
  { immediate: true },
);

function subscriberName(id: string) {
  return tenants.value.find((item) => item.id === id)?.name ?? 'Subscriber';
}
function followed(pipeline?: string) {
  const row = pipeline ? session.pipelineOf(pipeline) : undefined;
  return row
    ? { sourceId: row.sourceId, source: session.sourceName(row.sourceId), section: session.sectionLabel(row.id) }
    : undefined;
}
async function register() {
  if (await registerTenant(name.value.trim())) name.value = '';
}
function create() {
  void subscribe({ tenantId: tenantId.value, pipelineId: pipelineId.value });
}
</script>

<template>
  <div class="grid gap-6 xl:grid-cols-2">
    <Card class="self-start p-6">
      <h2 class="text-base font-semibold">Subscribers</h2>
      <p class="mt-1 text-sm text-muted-foreground">The products that receive posts.</p>
      <ul v-if="tenants.length" class="mt-4 divide-y divide-border" aria-label="Subscribers">
        <li v-for="tenant in tenants" :key="tenant.id" class="flex items-center justify-between gap-3 py-3">
          <div class="min-w-0">
            <p class="truncate font-medium">{{ tenant.name }}</p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ tenant.slug }} · since {{ dateOf(tenant.createdAtUtc) }}
            </p>
          </div>
          <Badge :variant="tenant.state === 'active' ? BadgeVariant.Success : BadgeVariant.Warning">
            {{ tenant.state }}
          </Badge>
        </li>
      </ul>
      <p v-else class="mt-4 text-sm text-muted-foreground">No subscribers yet.</p>
      <form
        v-if="can('tenants:manage')"
        class="mt-5 flex flex-wrap items-end gap-3 border-t border-border pt-5"
        aria-label="Register a subscriber"
        @submit.prevent="register"
      >
        <label class="block min-w-0 flex-1 space-y-2 text-sm">
          <span>New subscriber name</span>
          <TextInput v-model="name" placeholder="events" :is-required="true" />
        </label>
        <Button type="submit" variant="outline" :is-loading="pending">Register subscriber</Button>
      </form>
    </Card>

    <Card class="self-start p-6">
      <h2 class="text-base font-semibold">Subscriptions</h2>
      <p class="mt-1 text-sm text-muted-foreground">Which source and section each subscriber follows.</p>
      <ul v-if="subscriptions.length" class="mt-4 divide-y divide-border" aria-label="Subscriptions">
        <li
          v-for="subscription in subscriptions"
          :key="subscription.id"
          class="flex flex-wrap items-center justify-between gap-3 py-3"
        >
          <div class="min-w-0">
            <p class="font-medium">
              <ConsoleLink
                v-if="followed(subscription.pipelineId)"
                :to="{ kind: 'source', id: followed(subscription.pipelineId)!.sourceId }"
                class="hover:underline"
              >
                {{ followed(subscription.pipelineId)!.source }} · {{ followed(subscription.pipelineId)!.section }}
              </ConsoleLink>
              <span v-else>A source no longer in this workspace</span>
            </p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ subscriberName(subscription.tenantId) }} ·
              {{ subscription.mode === 'pull' ? 'pulls its posts' : subscription.mode }}
            </p>
          </div>
          <Badge :variant="subscription.state === 'active' ? BadgeVariant.Success : BadgeVariant.Warning">
            {{ subscription.state }}
          </Badge>
        </li>
      </ul>
      <p v-else class="mt-4 text-sm text-muted-foreground">No subscriptions in this workspace.</p>
      <form
        v-if="can('subscriptions:manage')"
        class="mt-5 space-y-4 border-t border-border pt-5"
        aria-label="Subscribe a subscriber"
        @submit.prevent="create"
      >
        <div class="grid gap-3 sm:grid-cols-3">
          <label class="block space-y-2 text-sm">
            <span>Subscriber</span>
            <ChoicePicker v-model="tenantId" :choices="subscriberChoices" label="Subscriber" placeholder="Choose" />
          </label>
          <label class="block space-y-2 text-sm">
            <span>Source</span>
            <ChoicePicker v-model="sourceId" :choices="sourceChoices" label="Source" placeholder="Choose" />
          </label>
          <label class="block space-y-2 text-sm">
            <span>Section</span>
            <ChoicePicker v-model="pipelineId" :choices="sectionChoices" label="Section" placeholder="Choose" />
          </label>
        </div>
        <Button type="submit" :is-disabled="!pipelineId || !tenantId" :is-loading="pending">Subscribe</Button>
      </form>
    </Card>
  </div>
</template>
