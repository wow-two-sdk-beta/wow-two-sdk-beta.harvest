<script setup lang="ts">
import { computed } from 'vue';
import { useHarvestConsole, type ConsolePlace } from './HarvestConsole';

/** Links to a console place through the host app's router; without navigation it renders its text only. */
defineOptions({ name: 'ConsoleLink' });
const props = defineProps<{ to: ConsolePlace }>();
defineSlots<{ default(): unknown }>();
const { navigation } = useHarvestConsole();
const href = computed(() => navigation.value?.href(props.to));

/** Opens the place in the app; a modified click keeps the browser's own behaviour, such as a new tab. @internal */
function follow(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
    return;
  event.preventDefault();
  navigation.value?.go(props.to);
}
</script>

<template>
  <a v-if="href" :href="href" @click="follow"><slot></slot></a>
  <span v-else><slot></slot></span>
</template>
