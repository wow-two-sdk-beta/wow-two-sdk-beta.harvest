<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Card } from '@wow-two-beta/fe/presentation/display';
import {
  Field,
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from '@wow-two-beta/fe/presentation/forms';
import RunList from './RunList.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { useHarvestConsole } from './HarvestConsole';

/** Every pipeline's latest runs, newest first, narrowed to one pipeline on request. */
defineOptions({ name: 'RunsPanel' });
const props = defineProps<{ pipeline?: string }>();
const { runs, pipelines, pipelineLabel } = useHarvestConsole();
/** Every pipeline, in the picker. @internal */
const AllPipelines = '*';
const filter = ref(props.pipeline || AllPipelines);
watch(
  () => props.pipeline,
  (value) => {
    filter.value = value || AllPipelines;
  },
);
const options = computed(() => [
  { itemKey: AllPipelines, value: AllPipelines, label: 'All pipelines' },
  ...pipelines.value.map((item) => ({ itemKey: item.id, value: item.id, label: pipelineLabel(item) })),
]);
const visible = computed(() =>
  filter.value === AllPipelines ? runs.value : runs.value.filter((run) => run.pipelineId === filter.value),
);
</script>

<template>
  <div class="space-y-6">
    <WorkerOffNotice />
    <Card class="p-6">
      <div class="mb-6 flex flex-wrap items-end justify-between gap-4">
        <p class="text-sm text-muted-foreground">{{ visible.length }} runs</p>
        <div class="w-64 max-w-full">
          <Field label="Pipeline">
            <SelectPicker v-model="filter" :options="options">
              <SelectPickerTrigger><SelectPickerValue placeholder="All pipelines" /></SelectPickerTrigger>
              <SelectPickerContent>
                <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
              </SelectPickerContent>
            </SelectPicker>
          </Field>
        </div>
      </div>
      <RunList :runs="visible" :show-pipeline="filter === AllPipelines" />
    </Card>
  </div>
</template>
