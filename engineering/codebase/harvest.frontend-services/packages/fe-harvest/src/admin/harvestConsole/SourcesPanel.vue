<script setup lang="ts">
import { Badge, BadgeVariant, Card, EmptyState } from '@wow-two-beta/fe/presentation/display';
import ConsoleLink from './ConsoleLink.vue';
import { stateVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/**
 * The read-only catalog of the sources SCFD's adapters registered in the open container: each source's adapter, what
 * it can do, and the sections a pipeline can follow. Sources are added with their adapters, never typed in here.
 */
defineOptions({ name: 'SourcesPanel' });
const { sources, sourceListState, capabilities, capabilityErrors, sectionsOf, canCapture, canWrite } =
  useHarvestConsole();

function readiness(id: string) {
  const metadata = capabilities.value[id];
  if (!metadata) return capabilityErrors.value[id] || 'Reading the adapter…';
  return metadata.configuration.readiness === 'ready'
    ? 'Ready on this runtime'
    : metadata.configuration.readiness === 'unavailable'
      ? 'Unavailable on this runtime'
      : 'Configuration required';
}
</script>

<template>
  <div class="space-y-6">
    <p v-if="sourceListState === 'loading'" class="text-sm text-muted-foreground" role="status">Loading sources…</p>
    <p v-else-if="sourceListState === 'unavailable'" class="text-sm text-muted-foreground" role="status">
      Could not load sources. The console retries every few seconds.
    </p>
    <Card v-else-if="!sources.length" class="p-2">
      <EmptyState
        title="No sources in this container"
        description="Sources come with the source adapters SCFD installs. Ask an operator to add one."
      />
    </Card>
    <ul v-else class="grid gap-4 lg:grid-cols-2" aria-label="Sources">
      <li v-for="source in sources" :key="source.id">
        <Card class="flex h-full flex-col gap-4 p-6">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <p class="truncate font-medium">{{ source.handle }}</p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ source.family }} · {{ source.coverage }} · {{ source.posture }}
              </p>
            </div>
            <Badge :variant="stateVariant(source.state)">{{ source.state }}</Badge>
          </div>
          <dl class="grid gap-3 text-sm sm:grid-cols-2">
            <div>
              <dt class="text-xs text-muted-foreground">Adapter</dt>
              <dd class="mt-1">{{ capabilities[source.id]?.configuration.displayName ?? '—' }}</dd>
              <dd class="mt-0.5 text-xs text-muted-foreground">{{ readiness(source.id) }}</dd>
            </div>
            <div>
              <dt class="text-xs text-muted-foreground">Operations</dt>
              <dd class="mt-1">{{ capabilities[source.id]?.workKinds.join(', ') || '—' }}</dd>
            </div>
          </dl>
          <div>
            <p class="text-xs text-muted-foreground">Sections</p>
            <ul v-if="sectionsOf(source.id).length" class="mt-2 flex flex-wrap gap-2" aria-label="Sections">
              <li v-for="section in sectionsOf(source.id)" :key="section.url" :title="section.url">
                <Badge :variant="BadgeVariant.Neutral">{{ section.label }}</Badge>
              </li>
            </ul>
            <p v-else class="mt-1 text-sm">Whole source</p>
          </div>
          <div v-if="canWrite('pipelines:manage') && canCapture(source.id)" class="mt-auto border-t border-border pt-4">
            <ConsoleLink :to="{ kind: 'pipelines', source: source.id }" class="text-sm text-primary hover:underline">
              New pipeline from this source
            </ConsoleLink>
          </div>
        </Card>
      </li>
    </ul>
  </div>
</template>
