<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant, Card } from '@wow-two-beta/fe/presentation/display';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import type { ContainerModel } from '../contracts';
import { createRegistryClient, type RegistryClient } from '../kit/CreateRegistryClient';

/**
 * Lists the containers a key may see — the console's first management screen, for the service's console or any console
 * that imports `./admin`. Without a key the service answers 401, and the panel says a key is needed.
 */
defineOptions({ name: 'ContainersPanel' });

const props = withDefaults(
  defineProps<{
    /** The registry to read; the consumer's own origin, without a key, when omitted. */
    client?: RegistryClient;
  }>(),
  { client: undefined },
);

const client = props.client ?? createRegistryClient();
const pending = shallowRef(true);
const containers = shallowRef<readonly ContainerModel[]>([]);
const failure = shallowRef<string | null>(null);
let controller: AbortController | null = null;

/** Loads the containers through the kit's client. */
async function load() {
  controller?.abort();
  const current = new AbortController();
  controller = current;
  pending.value = true;

  const result = await client.listContainers(current.signal);
  if (current.signal.aborted) return;
  pending.value = false;
  containers.value = result.ok ? result.value : [];
  failure.value = result.ok
    ? null
    : result.failure.status === 401
      ? 'An API key is needed to read the registry.'
      : result.failure.message;
}

onMounted(load);
onScopeDispose(() => controller?.abort());
</script>

<template>
  <Card class="p-6">
    <div class="flex flex-wrap items-center gap-3">
      <p class="font-medium">Containers</p>
      <Badge v-if="!pending && !failure" :variant="BadgeVariant.Neutral">{{ containers.length }}</Badge>
    </div>
    <p v-if="failure" class="mt-3 text-sm text-destructive" role="alert">{{ failure }}</p>
    <ul v-else-if="containers.length" class="mt-3 divide-y divide-border text-sm" aria-label="Containers">
      <li v-for="container in containers" :key="container.id" class="flex gap-3 py-2">
        <span class="font-medium">{{ container.kind }}</span>
        <span class="text-muted-foreground">{{ container.externalRef ?? container.id }}</span>
      </li>
    </ul>
    <p v-else-if="!pending" class="mt-3 text-sm text-muted-foreground">No containers yet.</p>
    <Button class="mt-4" variant="outline" :is-loading="pending" @click="load">Reload</Button>
  </Card>
</template>
