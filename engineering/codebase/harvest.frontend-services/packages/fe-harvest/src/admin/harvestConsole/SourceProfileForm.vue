<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { PipelineModel, PipelineUpdate } from '../../contracts';
import { useHarvestConsole } from './HarvestConsole';
import type { SourceProfileSchema } from './SourceProfileSchema';

/** Saves a profile and its run bounds; creating a profile never enables a schedule. */
defineOptions({ name: 'SourceProfileForm' });
const props = defineProps<{ sourceId: string; schema: SourceProfileSchema; pipeline?: PipelineModel }>();
const inputClass = 'w-full min-w-0 rounded-md border border-border bg-background px-3 py-2 text-sm';
const session = useHarvestConsole();
const formId = useId();
const url = ref('');
const values = ref<Record<string, number | string>>({});
const profileUrl = computed(() => props.schema.fixedUrl ?? url.value.trim());
const createLabel = computed(() => (props.schema.fixedUrl ? 'Create pipeline' : 'Add profile'));
const disabled = computed(
  () => session.pending.value || !session.canOperate.value || !session.canWrite('pipelines:manage'),
);
const valid = computed(() => {
  if (!props.pipeline) {
    try {
      const target = new URL(profileUrl.value);
      if (!['https:', 'http:'].includes(target.protocol) || target.username || target.password) return false;
    } catch {
      return false;
    }
  }
  return props.schema.limits.every((bound) => {
    const value = Number(values.value[bound.key]);
    return Number.isSafeInteger(value) && value >= bound.minimum && value <= bound.maximum;
  });
});
watch(
  () =>
    JSON.stringify([
      props.sourceId,
      props.schema.fixedUrl,
      props.pipeline?.id,
      props.pipeline?.limits.pageSize,
      props.pipeline?.limits.maxPages,
      props.schema.limits,
    ]),
  () => {
    values.value = Object.fromEntries(
      props.schema.limits.map((bound) => [bound.key, props.pipeline?.limits[bound.key] ?? bound.default]),
    );
  },
  { immediate: true },
);
watch([() => props.sourceId, () => props.schema.fixedUrl], () => {
  url.value = '';
});
async function save() {
  if (disabled.value || !valid.value) return;
  const limits: NonNullable<PipelineUpdate['limits']> = Object.fromEntries(
    props.schema.limits.map((bound) => [bound.key, Number(values.value[bound.key])]),
  );
  if (props.pipeline) await session.savePipelineLimits(props.pipeline.id, limits);
  else if (await session.declareCustomPipeline(props.sourceId, profileUrl.value, limits)) url.value = '';
}
</script>

<template>
  <form class="grid min-w-0 gap-3" :aria-label="pipeline ? 'Profile limits' : createLabel" @submit.prevent="save">
    <div v-if="!pipeline" class="grid gap-1.5">
      <p v-if="schema.fixedUrl" class="text-sm wrap-anywhere">{{ schema.fixedUrl }}</p>
      <label v-else :for="`${formId}-url`" class="text-sm font-medium">{{ schema.title }}</label>
      <input
        v-if="!schema.fixedUrl"
        :id="`${formId}-url`"
        v-model="url"
        type="url"
        required
        :class="inputClass"
        :disabled="disabled"
        :aria-describedby="`${formId}-description`"
      />
      <p :id="`${formId}-description`" class="text-xs text-muted-foreground">{{ schema.description }}</p>
    </div>
    <div class="flex flex-wrap items-end gap-3">
      <div v-for="bound in schema.limits" :key="bound.key" class="grid min-w-28 flex-1 gap-1.5">
        <label :for="`${formId}-${bound.key}`" class="text-sm font-medium">{{ bound.title }}</label>
        <input
          :id="`${formId}-${bound.key}`"
          v-model="values[bound.key]"
          type="number"
          required
          step="1"
          :min="bound.minimum"
          :max="bound.maximum"
          :class="inputClass"
          :disabled="disabled"
          :aria-describedby="`${formId}-${bound.key}-range`"
        />
        <p :id="`${formId}-${bound.key}-range`" class="text-xs text-muted-foreground">
          {{ bound.minimum }}–{{ bound.maximum }}
        </p>
      </div>
      <Button type="submit" variant="outline" :is-disabled="disabled || !valid">
        {{ pipeline ? 'Save limits' : createLabel }}
      </Button>
    </div>
    <p v-if="!pipeline" class="text-xs text-muted-foreground">Saved for manual runs. No harvest is scheduled.</p>
  </form>
</template>
