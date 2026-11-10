<script setup lang="ts">
import { computed } from 'vue';
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from '@wow-two-beta/fe/presentation/forms';

/** A compact select over a few named choices, such as `every 6 hours`; a pick emits at once. */
defineOptions({ name: 'ChoicePicker' });
const props = withDefaults(
  defineProps<{
    modelValue: string;
    choices: readonly { readonly value: string; readonly label: string }[];
    /** The control's accessible name. */
    label: string;
    isDisabled?: boolean;
    /** Shown while the value matches no choice. */
    placeholder?: string;
  }>(),
  { isDisabled: false, placeholder: undefined },
);
const emit = defineEmits<{ 'update:modelValue': [value: string] }>();
const options = computed(() => props.choices.map((item) => ({ itemKey: item.value, ...item })));

function pick(value: string | null) {
  if (value !== null && value !== props.modelValue) emit('update:modelValue', value);
}
</script>

<template>
  <SelectPicker
    :model-value="props.modelValue"
    :options="options"
    :is-disabled="props.isDisabled"
    @update:model-value="pick"
  >
    <SelectPickerTrigger v-bind="{ 'aria-label': props.label }" class="min-w-0">
      <SelectPickerValue :placeholder="props.placeholder" />
    </SelectPickerTrigger>
    <SelectPickerContent>
      <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
    </SelectPickerContent>
  </SelectPicker>
</template>
