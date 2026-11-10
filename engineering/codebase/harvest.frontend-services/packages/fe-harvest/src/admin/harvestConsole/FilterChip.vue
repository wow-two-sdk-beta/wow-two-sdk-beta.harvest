<script setup lang="ts">
/** One filter as a pill button with its count; pressed, it narrows the list. */
defineOptions({ name: 'FilterChip' });
const props = defineProps<{ label: string; count: number; isPressed: boolean }>();
const emit = defineEmits<{ press: [] }>();
</script>

<template>
  <button
    type="button"
    class="inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full border px-3.5 text-sm font-semibold whitespace-nowrap transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
    :class="
      props.isPressed
        ? 'border-primary bg-primary text-primary-foreground'
        : 'border-border bg-card text-foreground hover:border-primary/40'
    "
    :aria-pressed="props.isPressed"
    @click="emit('press')"
  >
    {{ props.label }}
    <span class="tabular-nums" :class="props.isPressed ? '' : 'text-muted-foreground'">{{ props.count }}</span>
  </button>
</template>
