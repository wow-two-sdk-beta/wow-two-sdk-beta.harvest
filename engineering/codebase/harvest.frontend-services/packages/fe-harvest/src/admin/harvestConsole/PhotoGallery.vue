<script setup lang="ts">
import { computed } from 'vue';
import { LightboxModal } from '@wow-two-beta/fe/presentation/overlays';

const props = withDefaults(
  defineProps<{
    images: readonly string[];
    label?: string;
    isMuted?: boolean;
  }>(),
  { label: 'Photos', isMuted: false },
);

const photos = computed(() => props.images.map((src, index) => ({ src, alt: `Photo ${index + 1}` })));
const previews = computed(() => photos.value.slice(0, 5));
const overflow = computed(() => Math.max(0, photos.value.length - previews.value.length));
</script>

<template>
  <section v-if="photos.length" :aria-label="label" v-bind="{ 'data-post-images': photos.length }">
    <LightboxModal v-slot="{ openAt }" :images="photos" :label="label">
      <div
        class="grid grid-cols-2 gap-2 overflow-hidden rounded-2xl sm:h-[clamp(16rem,37vw,30rem)]"
        :class="
          previews.length === 1 ? 'sm:grid-cols-1' : previews.length < 4 ? 'sm:grid-cols-[2fr_1fr]' : 'sm:grid-cols-4'
        "
      >
        <button
          v-for="(photo, index) in previews"
          :key="index"
          type="button"
          class="group relative min-h-0 min-w-0 overflow-hidden bg-muted focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-offset-[-3px] focus-visible:outline-white"
          :class="[
            index === 0 ? 'col-span-2 aspect-[16/9] sm:row-span-2 sm:aspect-auto' : 'aspect-[3/2] sm:aspect-auto',
            index === 0 && previews.length < 4 ? 'sm:col-span-1' : '',
            previews.length === 4 && index === 3 ? 'sm:col-span-2' : '',
            previews.length === 2 && index === 1 ? 'sm:row-span-2' : '',
          ]"
          :aria-label="
            overflow && index === previews.length - 1 ? `View all ${photos.length} photos` : `Open photo ${index + 1}`
          "
          @click="openAt(index)"
        >
          <img
            :src="photo.src"
            alt=""
            decoding="async"
            referrerpolicy="no-referrer"
            :loading="index ? 'lazy' : 'eager'"
            class="absolute inset-0 size-full transition duration-200 group-hover:brightness-90"
            :class="[previews.length === 1 ? 'object-contain' : 'object-cover', { grayscale: isMuted }]"
          />
          <span
            v-if="overflow && index === previews.length - 1"
            aria-hidden="true"
            class="absolute inset-0 grid place-items-center bg-black/65 text-2xl font-semibold text-white sm:text-3xl"
          >
            +{{ overflow }}
          </span>
        </button>
      </div>
    </LightboxModal>
  </section>
</template>
