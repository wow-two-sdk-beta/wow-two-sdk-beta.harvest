<script setup lang="ts">
import { watch, type ImgHTMLAttributes } from 'vue';
import { useSourceImages } from './UseSourceImages';

const props = defineProps<{
  src: string;
  alt?: string;
  loading?: 'lazy' | 'eager';
  decoding?: 'async' | 'sync' | 'auto';
  referrerpolicy?: ImgHTMLAttributes['referrerpolicy'];
}>();
const emit = defineEmits<{ error: [] }>();
const images = useSourceImages(() => [props.src]);
watch(
  () => images.failed.value.has(props.src),
  (failed) => {
    if (failed) emit('error');
  },
);
</script>

<template>
  <img
    v-if="images.urls.value[0]"
    :src="images.urls.value[0]"
    :alt="props.alt ?? ''"
    :loading="props.loading"
    :decoding="props.decoding"
    :referrerpolicy="props.referrerpolicy"
    @error="emit('error')"
  />
</template>
