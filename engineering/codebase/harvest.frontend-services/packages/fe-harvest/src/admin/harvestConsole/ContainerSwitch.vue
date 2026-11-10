<script setup lang="ts">
import { computed } from 'vue';
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from '@wow-two-beta/fe/presentation/forms';
import { useHarvestConsole } from './HarvestConsole';

/** Switches the open container — the pool or a tenant's private one; sized for the top bar. */
defineOptions({ name: 'ContainerSwitch' });
const { containers, containerId } = useHarvestConsole();
const options = computed(() =>
  containers.value.map((item) => ({
    itemKey: item.id,
    value: item.id,
    label: item.externalRef ?? `${item.kind} · ${item.id.slice(0, 8)}`,
  })),
);
</script>

<template>
  <div v-if="options.length" class="w-56 max-w-full">
    <SelectPicker v-model="containerId" :options="options">
      <SelectPickerTrigger v-bind="{ 'aria-label': 'Container' }">
        <SelectPickerValue placeholder="Choose a container" />
      </SelectPickerTrigger>
      <SelectPickerContent>
        <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
      </SelectPickerContent>
    </SelectPicker>
  </div>
</template>
