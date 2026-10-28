<script setup lang="ts">
import type { SourceCapabilitiesModel } from '../../contracts';

/** Displays declarative metadata from the selected runtime without importing its implementations. */
defineOptions({ name: 'SourceCapabilitiesPanel' });
defineProps<{ capabilities?: SourceCapabilitiesModel; error?: string }>();
</script>

<template>
  <section aria-label="Source capabilities" class="space-y-3 rounded-xl border border-border p-4 text-sm">
    <template v-if="capabilities">
      <p class="font-medium">{{ capabilities.configuration.displayName }}</p>
      <p>
        {{
          capabilities.configuration.readiness === 'ready'
            ? 'Configured locally'
            : capabilities.configuration.readiness === 'unavailable'
              ? 'Unavailable on this runtime'
              : 'Configuration required'
        }}
      </p>
      <p v-if="capabilities.configuration.message" class="text-muted-foreground">
        {{ capabilities.configuration.message }}
      </p>
      <p>Available operations: {{ capabilities.workKinds.join(', ') || 'None installed' }}</p>
      <details>
        <summary class="cursor-pointer">Saved scope schema</summary>
        <pre class="mt-3 max-h-64 overflow-auto whitespace-pre-wrap break-all text-xs">{{
          JSON.stringify(capabilities.configuration.scopeSchema, null, 2)
        }}</pre>
      </details>
    </template>
    <p v-else role="status">{{ error ?? 'Loading source capabilities…' }}</p>
  </section>
</template>
