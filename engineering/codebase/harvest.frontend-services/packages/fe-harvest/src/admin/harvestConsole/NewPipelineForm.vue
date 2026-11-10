<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { NumberInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import type { PipelineModel } from '../../contracts';
import SourceCapabilitiesPanel from './SourceCapabilitiesPanel.vue';
import { useHarvestConsole } from './HarvestConsole';

/** The whole source, chosen instead of one of its sections. @internal */
const WholeSource = '*';

/**
 * Starts a pipeline by choosing a source and one of its sections — never by typing a URL. The sections come from the
 * source's adapter; a source whose adapter lists none is followed whole.
 */
defineOptions({ name: 'NewPipelineForm' });
const props = defineProps<{ initialSourceId?: string }>();
const emit = defineEmits<{ created: [pipeline: PipelineModel]; cancel: [] }>();
const { sources, sourceListState, capabilities, capabilityErrors, sectionsOf, canCapture, pending, declarePipeline } =
  useHarvestConsole();

const sourceId = ref(props.initialSourceId ?? '');
const section = ref('');
const name = ref('');
const everyMinutes = ref(60);
const scheduleEnabled = ref(false);

const source = computed(() => sources.value.find((item) => item.id === sourceId.value));
const metadata = computed(() => capabilities.value[sourceId.value]);
const sections = computed(() => sectionsOf(sourceId.value));
/** Whether the adapter needs a starting URL, so the whole source is not a valid choice. */
const needsSection = computed(() => {
  const required = metadata.value?.configuration.scopeSchema.required;
  return Array.isArray(required) && required.includes('url');
});
const choices = computed(() => [
  ...sections.value.map((item) => ({ value: item.url, label: item.label })),
  ...(metadata.value && !needsSection.value ? [{ value: WholeSource, label: 'Whole source' }] : []),
]);
const chosen = computed(() => choices.value.find((item) => item.value === section.value));
const canCreate = computed(() => !!source.value && !!chosen.value && canCapture(sourceId.value));

watch(
  () => props.initialSourceId,
  (value) => {
    if (value) sourceId.value = value;
  },
);
/* A new source starts with its first section, and no source starts with the first one. */
watch(
  [sources, sourceId],
  () => {
    if (!sourceId.value && sources.value.length) sourceId.value = sources.value[0]!.id;
  },
  { immediate: true },
);
watch(
  choices,
  (items) => {
    if (!items.some((item) => item.value === section.value)) section.value = items[0]?.value ?? '';
  },
  { immediate: true },
);

async function create() {
  if (!canCreate.value || !source.value || !chosen.value) return;
  const created = await declarePipeline({
    sourceId: sourceId.value,
    name: name.value.trim() || `${source.value.handle} · ${chosen.value.label}`,
    ...(chosen.value.value === WholeSource ? {} : { sectionUrl: chosen.value.value }),
    everyMinutes: everyMinutes.value,
    scheduleEnabled: scheduleEnabled.value,
  });
  if (created) emit('created', created);
}
</script>

<template>
  <Card class="p-6">
    <h2 class="text-base font-semibold">New pipeline</h2>
    <p class="mt-1 text-sm text-muted-foreground">Choose a source, then the section of it to follow.</p>
    <form class="mt-6 space-y-6" aria-label="New pipeline" @submit.prevent="create">
      <fieldset class="space-y-3">
        <legend class="text-sm font-medium">Source</legend>
        <p v-if="sourceListState === 'loading'" class="text-sm text-muted-foreground" role="status">
          Loading registered sources…
        </p>
        <p v-else-if="sourceListState === 'unavailable'" class="text-sm text-muted-foreground" role="status">
          Could not load sources. The console retries every few seconds.
        </p>
        <p v-else-if="!sources.length" class="text-sm text-muted-foreground">
          No sources registered in this container. Sources come with SCFD's source adapters; ask an operator to add one.
        </p>
        <div v-else class="grid gap-2 sm:grid-cols-2" role="group" aria-label="Source">
          <button
            v-for="item in sources"
            :key="item.id"
            type="button"
            class="rounded-lg border p-3 text-left text-sm transition-colors focus-visible:outline-2 focus-visible:outline-primary"
            :class="item.id === sourceId ? 'border-primary bg-primary-soft' : 'border-border hover:bg-muted'"
            :aria-pressed="item.id === sourceId"
            @click="sourceId = item.id"
          >
            <span class="block font-medium">{{ item.handle }}</span>
            <span class="mt-1 block text-xs text-muted-foreground">
              {{ capabilities[item.id]?.configuration.displayName ?? item.family }}
            </span>
          </button>
        </div>
      </fieldset>

      <fieldset v-if="source" class="space-y-3">
        <legend class="text-sm font-medium">Section</legend>
        <p v-if="!metadata" class="text-sm text-muted-foreground" role="status">
          {{ capabilityErrors[sourceId] || 'Reading the source’s sections…' }}
        </p>
        <p v-else-if="!choices.length" class="text-sm text-muted-foreground">
          This source’s adapter lists no sections yet, and it cannot follow the whole source.
        </p>
        <div v-else class="flex flex-wrap gap-2" role="group" aria-label="Section">
          <Button
            v-for="item in choices"
            :key="item.value"
            type="button"
            size="sm"
            :variant="item.value === section ? 'solid' : 'outline'"
            :aria-pressed="item.value === section"
            @click="section = item.value"
          >
            {{ item.label }}
          </Button>
        </div>
        <SourceCapabilitiesPanel
          v-if="metadata && !canCapture(sourceId)"
          :capabilities="metadata"
          :error="capabilityErrors[sourceId]"
        />
      </fieldset>

      <div class="grid gap-4 sm:grid-cols-2">
        <label class="block space-y-2 text-sm">
          <span>Name (optional)</span>
          <TextInput
            v-model="name"
            :placeholder="source && chosen ? `${source.handle} · ${chosen.label}` : 'Upcoming events'"
          />
        </label>
        <label class="block space-y-2 text-sm">
          <span>Run every (minutes)</span>
          <NumberInput v-model="everyMinutes" :min="5" :max="525600" />
        </label>
      </div>
      <Button
        type="button"
        size="sm"
        variant="outline"
        :aria-pressed="scheduleEnabled"
        @click="scheduleEnabled = !scheduleEnabled"
      >
        {{ scheduleEnabled ? 'Scheduled capture enabled' : 'Manual capture only' }}
      </Button>
      <div class="flex flex-wrap gap-3 border-t border-border pt-5">
        <Button type="submit" :is-disabled="!canCreate" :is-loading="pending">Create pipeline</Button>
        <Button type="button" variant="ghost" @click="emit('cancel')">Cancel</Button>
      </div>
    </form>
  </Card>
</template>
