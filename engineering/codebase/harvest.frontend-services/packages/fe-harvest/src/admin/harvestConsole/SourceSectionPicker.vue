<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { SourceCapabilitiesModel } from '../../contracts';
import { sameSection, sourceSections } from './SourceSections';

defineOptions({ name: 'SourceSectionPicker' });
const props = defineProps<{ capabilities?: SourceCapabilitiesModel }>();
const url = defineModel<string>({ required: true });
const sections = computed(() => {
  const options = sourceSections(props.capabilities);
  const language = URL.canParse(url.value) ? new URL(url.value).pathname.split('/')[1] : undefined;
  const locale = options.find((section) => section.locale === language)?.locale ?? options[0]?.locale;
  return options.filter((section) => section.locale === undefined || section.locale === locale);
});

function choose(sectionUrl: string) {
  if (!sameSection(url.value, sectionUrl)) url.value = sectionUrl;
}
</script>

<template>
  <div v-if="sections.length" class="flex flex-wrap gap-2" role="group" aria-label="Collection section">
    <Button
      v-for="section in sections"
      :key="section.url"
      type="button"
      size="sm"
      :variant="sameSection(url, section.url) ? 'solid' : 'outline'"
      :aria-pressed="sameSection(url, section.url)"
      @click="choose(section.url)"
    >
      {{ section.label }}
    </Button>
  </div>
</template>
