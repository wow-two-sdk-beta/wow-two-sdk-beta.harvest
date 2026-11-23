<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import type { SourceImagePolicy, SourceImagePreset } from '../../contracts/SourceImagePolicy';
import { useHarvestConsole } from './HarvestConsole';

/** Configures the sizes the media worker generates for newly collected post photos and posters. */
defineOptions({ name: 'SourceImagePolicyPanel' });
const session = useHarvestConsole();
const presets: readonly { name: SourceImagePreset; label: string }[] = [
  { name: 's1', label: 'Thumbnail' },
  { name: 's2', label: 'Small card' },
  { name: 's3', label: 'Card' },
  { name: 's4', label: 'Wide card' },
  { name: 's5', label: 'Detail' },
];
const policy = shallowRef<SourceImagePolicy>();
const selection = ref<SourceImagePreset[]>([]);
const failure = ref('');
const status = ref('');
const saving = ref(false);
const canEdit = computed(() => session.canOperate.value && session.can('tenants:manage'));
const isDirty = computed(() =>
  policy.value
    ? presets.some(({ name }) => selection.value.includes(name) !== policy.value?.postPresets.includes(name))
    : false,
);
let request: AbortController | undefined;

function apply(value: SourceImagePolicy) {
  policy.value = value;
  selection.value = [...value.postPresets];
}

watch(
  [session.client, canEdit],
  async ([api, allowed], _before, onCleanup) => {
    const controller = new AbortController();
    request = controller;
    onCleanup(() => controller.abort());
    policy.value = undefined;
    failure.value = '';
    status.value = '';
    saving.value = false;
    if (!allowed || !api) return;
    if (!api.getSourceImagePolicy) {
      failure.value = 'This connection does not expose the media policy.';
      return;
    }
    const result = await api.getSourceImagePolicy(controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) apply(result.value);
    else failure.value = result.failure.message || 'The media policy could not be loaded.';
  },
  { immediate: true },
);

async function save() {
  const api = session.client.value;
  const signal = request?.signal;
  if (!api?.saveSourceImagePolicy || !signal || saving.value || !isDirty.value) return;
  saving.value = true;
  status.value = '';
  failure.value = '';
  const result = await api.saveSourceImagePolicy(selection.value, signal);
  if (signal.aborted) return;
  saving.value = false;
  if (result.ok) {
    apply(result.value);
    status.value = 'Saved for newly collected posts.';
  } else failure.value = result.failure.message || 'The media policy could not be saved.';
}
</script>

<template>
  <Card class="max-w-2xl p-6">
    <p v-if="!canEdit" class="text-sm text-muted-foreground">Only SCFD operators can change this policy.</p>
    <form v-else-if="policy" class="space-y-5" @submit.prevent="save">
      <p v-if="!policy.filesConfigured" role="status" class="text-sm text-muted-foreground">
        The Files connection is off. The saved sizes will apply when it is enabled.
      </p>
      <fieldset :disabled="saving">
        <legend class="font-semibold">Post image sizes</legend>
        <p class="mt-1 text-sm text-muted-foreground">
          Generate one WebP per selected size for new post images. Files keeps the original and skips sizes wider than
          it.
        </p>
        <div class="mt-4 grid gap-2">
          <label
            v-for="preset in presets"
            :key="preset.name"
            class="flex min-h-10 cursor-pointer items-center gap-3 rounded-lg border border-border px-3 hover:bg-muted"
          >
            <input v-model="selection" type="checkbox" :value="preset.name" class="size-4 accent-primary" />
            <span class="font-mono text-sm font-semibold">{{ preset.name }}</span>
            <span class="text-sm">{{ preset.label }}</span>
          </label>
        </div>
      </fieldset>
      <p class="text-sm text-muted-foreground">Source branding keeps its original size.</p>
      <div class="flex flex-wrap items-center gap-3">
        <Button type="submit" :is-disabled="!isDirty || saving" :is-loading="saving">Save sizes</Button>
        <p v-if="status" role="status" class="text-sm text-muted-foreground">{{ status }}</p>
      </div>
    </form>
    <p v-else-if="!failure" role="status" class="text-sm text-muted-foreground">Loading media policy…</p>
    <p v-if="failure" role="alert" class="mt-3 text-sm text-destructive">{{ failure }}</p>
  </Card>
</template>
