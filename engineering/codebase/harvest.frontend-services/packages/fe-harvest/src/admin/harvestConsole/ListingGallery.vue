<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useSourceImages } from './UseSourceImages';

const props = defineProps<{ images: readonly string[]; removed: boolean }>();
const broken = ref<ReadonlySet<string>>(new Set());
const images = useSourceImages(() => props.images);
const photos = computed(() => images.urls.value.filter((url) => !broken.value.has(url)));
const chosen = ref<string>();
const active = computed(() => photos.value.find((url) => url === chosen.value) ?? photos.value[0]);
const number = computed(() => (active.value ? photos.value.indexOf(active.value) + 1 : 0));
watch(
  () => props.images.join('\n'),
  () => {
    broken.value = new Set();
    chosen.value = undefined;
  },
);
function markBroken(url: string) {
  broken.value = new Set([...broken.value, url]);
}
function markMainImageBroken(event: Event) {
  const url = (event.currentTarget as HTMLImageElement).getAttribute('src');
  if (url) markBroken(url);
}
</script>

<template>
  <section
    v-if="active"
    aria-label="Property photos"
    class="grid min-w-0 gap-2.5"
    :class="photos.length > 1 ? 'sm:grid-cols-[minmax(0,1fr)_6rem]' : ''"
  >
    <img
      :key="active"
      :src="active"
      :alt="`Property photo ${number} of ${photos.length}`"
      class="aspect-[4/3] max-h-[32rem] w-full rounded-2xl bg-muted object-contain sm:aspect-auto sm:h-[min(50vw,32rem)]"
      :class="{ grayscale: removed }"
      decoding="async"
      referrerpolicy="no-referrer"
      @error="markMainImageBroken"
    />
    <div
      v-if="photos.length > 1"
      class="flex min-w-0 gap-2 overflow-x-auto sm:max-h-[min(50vw,32rem)] sm:flex-col sm:overflow-x-hidden sm:overflow-y-auto"
    >
      <button
        v-for="(url, item) in photos"
        :key="url"
        type="button"
        :aria-label="`Show property photo ${item + 1}`"
        :aria-pressed="active === url"
        class="h-16 w-24 shrink-0 overflow-hidden rounded-xl border-2 border-transparent bg-muted focus-visible:outline-2 focus-visible:outline-ring aria-pressed:border-primary sm:w-full"
        @click="chosen = url"
      >
        <img
          :src="url"
          alt=""
          class="size-full object-cover"
          :class="{ grayscale: removed }"
          loading="lazy"
          decoding="async"
          referrerpolicy="no-referrer"
          @error="markBroken(url)"
        />
      </button>
    </div>
  </section>
</template>
