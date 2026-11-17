<script setup lang="ts" generic="Value extends string">
import { computed } from 'vue';
import {
  SelectPicker,
  SelectPickerContent,
  SelectPickerItem,
  SelectPickerTrigger,
  SelectPickerValue,
} from '@wow-two-beta/fe/presentation/forms';

/** A named choice control that emits the selected value. */
defineOptions({ name: 'ChoicePicker' });
const props = withDefaults(
  defineProps<{
    modelValue: Value;
    choices: readonly { readonly value: Value; readonly label: string; readonly text?: string }[];
    /** The control's accessible name. */
    label: string;
    isDisabled?: boolean;
    isSearchable?: boolean;
    hasTriggerWidth?: boolean;
    /** Shown while the value matches no choice. */
    placeholder?: string;
  }>(),
  { isDisabled: false, isSearchable: false, hasTriggerWidth: false, placeholder: undefined },
);
const emit = defineEmits<{ 'update:modelValue': [value: Value] }>();
const options = computed(() => props.choices.map((item) => ({ itemKey: item.value, ...item })));

function pick(value: Value | null) {
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
    <SelectPickerContent :is-searchable="props.isSearchable" :has-trigger-width="props.hasTriggerWidth">
      <SelectPickerItem v-for="option in options" :key="option.itemKey" v-bind="option" />
    </SelectPickerContent>
  </SelectPicker>
</template>
