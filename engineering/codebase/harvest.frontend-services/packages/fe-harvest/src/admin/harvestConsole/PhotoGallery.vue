<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { LightboxModal } from '@wow-two-beta/fe/presentation/overlays';
import { useSourceImages } from './UseSourceImages';

/** The lead stands alone. Additional photos stack in reading order within each column. */
const PreviewColumns: readonly (readonly (readonly number[])[])[] = [
  [],
  [[0]],
  [[0], [1]],
  [[0], [1, 2]],
  [[0], [1], [2, 3]],
  [[0], [1, 2], [3, 4]],
];
const DefaultAspect = 4 / 5;
const GapRem = 0.5;
const PreviewLimit = PreviewColumns.length - 1;
// A transparent pixel keeps the viewer from requesting a private path while its image resolves.
const PendingImage = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

const props = withDefaults(
  defineProps<{
    images: readonly string[];
    label?: string;
    isMuted?: boolean;
    fixedHeight?: boolean;
  }>(),
  { label: 'Photos', isMuted: false, fixedHeight: false },
);

const isOpen = ref(false);
const selectedIndex = ref(0);
const selectedSource = computed(() =>
  isOpen.value ? props.images[Math.min(selectedIndex.value, props.images.length - 1)] : undefined,
);
const lead = useSourceImages(
  () => props.images.slice(0, 1),
  () => 's3',
);
const supporting = useSourceImages(
  () => props.images.slice(1, PreviewLimit),
  () => 's2',
);
const thumbnails = useSourceImages(
  () => (isOpen.value && props.images.length > 1 ? props.images : []),
  () => 's1',
);
const original = useSourceImages(() => (selectedSource.value ? [selectedSource.value] : []));
const aspects = reactive(new Map<string, number>());

const previews = computed(() =>
  props.images.slice(0, PreviewLimit).map((source, index) => {
    const images = index === 0 ? lead : supporting;
    return {
      source,
      index,
      src: images.srcFor(source),
      unavailable: images.failed.value.has(source),
      aspect: aspects.get(source) ?? DefaultAspect,
    };
  }),
);
const overflow = computed(() => props.images.length - previews.value.length);
const photos = computed(() =>
  props.images.map((source, index) => {
    const preview = previews.value[index]?.src;
    const thumbnail = thumbnails.srcFor(source) ?? preview;
    const src = original.srcFor(source) ?? preview ?? thumbnail;
    const unavailable = original.failed.value.has(source);
    return {
      src: src ?? PendingImage,
      thumbnailSrc: thumbnail ?? PendingImage,
      alt: `Photo ${index + 1}`,
      caption: unavailable
        ? src
          ? 'Full-size photo unavailable'
          : 'Photo unavailable'
        : src
          ? undefined
          : 'Loading photo…',
    };
  }),
);

function readAspect(source: string, event: Event) {
  const image = event.currentTarget as HTMLImageElement;
  const aspect = image.naturalWidth / image.naturalHeight;
  if (Number.isFinite(aspect) && aspect > 0) aspects.set(source, Math.min(2.5, Math.max(0.45, aspect)));
}

const columns = computed(() =>
  PreviewColumns[previews.value.length]!.map((indices) => indices.map((index) => previews.value[index]!)),
);

const mosaicStyle = computed(() => {
  // Stacked pictures share a width, so their height per unit of width adds up.
  const weights = columns.value.map((column) => 1 / column.reduce((height, photo) => height + 1 / photo.aspect, 0));
  const aspect = weights.reduce((width, weight) => width + weight, 0) || DefaultAspect;
  const gutters = Math.max(0, weights.length - 1) * GapRem;
  const maxHeight = props.fixedHeight ? '24rem' : '30rem';
  return {
    '--mosaic-columns': weights.map((weight) => `minmax(0, ${weight}fr)`).join(' '),
    '--mosaic-height': `min(${maxHeight}, calc((100cqw - ${gutters}rem) / ${aspect}))`,
    '--mosaic-width': `calc(var(--mosaic-height) * ${aspect} + ${gutters}rem)`,
  };
});
</script>

<template>
  <section
    v-if="photos.length"
    :aria-label="label"
    class="@container min-w-0"
    v-bind="{ 'data-post-images': photos.length }"
  >
    <LightboxModal
      v-slot="{ openAt }"
      v-model:open="isOpen"
      v-model:index="selectedIndex"
      :images="photos"
      :label="label"
    >
      <div
        class="mx-auto flex max-w-full snap-x snap-proximity gap-2 overflow-x-auto overscroll-x-contain @xl:grid @xl:h-[var(--mosaic-height)] @xl:w-[var(--mosaic-width)] @xl:grid-cols-[var(--mosaic-columns)] @xl:overflow-visible"
        :class="{ 'justify-center': previews.length === 1 }"
        :style="mosaicStyle"
      >
        <div
          v-for="column in columns"
          :key="column[0]!.index"
          class="contents @xl:flex @xl:min-h-0 @xl:min-w-0 @xl:flex-col @xl:gap-2"
        >
          <button
            v-for="photo in column"
            :key="photo.index"
            type="button"
            class="group relative h-56 w-[var(--photo-width)] shrink-0 snap-start overflow-hidden rounded-xl bg-muted/60 focus-visible:z-10 focus-visible:outline-2 focus-visible:outline-offset-[-3px] focus-visible:outline-white @xl:h-auto @xl:min-h-0 @xl:w-full @xl:flex-[var(--photo-grow)_1_0%]"
            :style="{ '--photo-grow': 1 / photo.aspect, '--photo-width': `min(80cqw, ${14 * photo.aspect}rem)` }"
            :aria-label="
              overflow && photo.index === previews.length - 1
                ? `View all ${photos.length} photos`
                : `Open photo ${photo.index + 1}`
            "
            @click="openAt(photo.index)"
          >
            <img
              v-if="photo.src"
              :src="photo.src"
              alt=""
              decoding="async"
              referrerpolicy="no-referrer"
              :loading="photo.index ? 'lazy' : 'eager'"
              class="absolute inset-0 size-full object-contain transition duration-200 group-hover:brightness-90"
              :class="{ grayscale: isMuted }"
              @load="readAspect(photo.source, $event)"
            />
            <span
              v-else-if="photo.unavailable"
              class="absolute inset-0 grid place-items-center text-sm text-muted-foreground"
            >
              Photo unavailable
            </span>
            <span
              v-if="overflow && photo.index === previews.length - 1"
              aria-hidden="true"
              class="pointer-events-none absolute inset-0 grid place-items-center bg-black/60 text-2xl font-semibold text-white sm:text-3xl"
            >
              +{{ overflow }}
            </span>
          </button>
        </div>
      </div>
    </LightboxModal>
  </section>
</template>
