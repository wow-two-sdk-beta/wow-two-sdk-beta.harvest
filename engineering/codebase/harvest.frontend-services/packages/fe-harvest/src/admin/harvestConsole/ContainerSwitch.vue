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

/**
 * Switches the open workspace — the shared pool or a subscriber's private one — sized for the top bar. It shows only
 * while there is more than one to choose from.
 */
defineOptions({ name: 'ContainerSwitch' });
const { containers, containerId, tenants } = useHarvestConsole();
const options = computed(() =>
  containers.value.map((item) => ({
    itemKey: item.id,
    value: item.id,
    label:
      item.externalRef ??
      (item.kind === 'pool'
        ? 'Shared pool'
        : `Private · ${tenants.value.find((tenant) => tenant.id === item.tenantId)?.name ?? 'subscriber'}`),
  })),
);
</script>

<template>
  <div v-if="options.length > 1" class="w-56 max-w-full">
    <SelectPicker v-model="containerId" :options="options">
      <SelectPickerTrigger v-bind="{ 'aria-label': 'Workspace' }">
        <SelectPickerValue placeholder="Choose a workspace" />
      </SelectPickerTrigger>
      <SelectPickerContent>
        <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
      </SelectPickerContent>
    </SelectPicker>
  </div>
</template>
