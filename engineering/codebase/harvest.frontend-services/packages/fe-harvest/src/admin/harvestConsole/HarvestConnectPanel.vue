<script setup lang="ts">
import { ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { PasswordInput } from '@wow-two-beta/fe/presentation/forms';
import { useHarvestConsole } from './HarvestConsole';

/**
 * The signed-out screen's card: Identity sign-in, the embedding app's message, or an API key form for a console that
 * owns its connection. It shows while the session connects, too.
 */
defineOptions({ name: 'HarvestConnectPanel' });
const props = defineProps<{
  /** Identity's sign-in address; with it the card offers sign-in instead of a key form. */
  signInPath?: string;
  /** Why the console is not connected, from the embedding app. */
  connectionMessage?: string;
}>();
const { connecting, connectionManaged, hasConnectionStorage, pending, failure, connect } = useHarvestConsole();
const key = ref('');

async function submit() {
  const value = key.value;
  key.value = '';
  await connect(value);
}
</script>

<template>
  <Card class="w-full max-w-xl p-7 sm:p-9">
    <p v-if="connecting" role="status">Connecting to SCFD…</p>
    <template v-else>
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-primary">Your sources, on schedule</p>
      <h1 class="mt-3 text-2xl font-semibold">{{ props.signInPath ? 'Sign in to SCFD' : 'Connect to SCFD' }}</h1>
      <p class="mt-2 text-sm leading-relaxed text-muted-foreground">
        Bring your pipelines, captures and deliveries into one place.
      </p>
      <p
        v-if="failure"
        class="mt-5 rounded-xl border border-destructive/25 bg-card p-4 text-sm text-destructive"
        role="alert"
      >
        {{ failure }}
      </p>
      <a
        v-if="props.signInPath"
        :href="props.signInPath"
        class="mt-7 inline-flex rounded-lg bg-primary px-5 py-3 text-sm font-medium text-primary-foreground"
      >
        Sign in
      </a>
      <p v-if="props.signInPath && props.connectionMessage" class="mt-4 text-sm" role="status">
        {{ props.connectionMessage }}
      </p>
      <p v-if="!props.signInPath && connectionManaged" class="mt-7 text-sm">
        {{ props.connectionMessage ?? 'Restore the connection through the embedding application.' }}
      </p>
      <form v-else-if="!props.signInPath" class="mt-7 space-y-4" @submit.prevent="submit">
        <label class="block space-y-2 text-sm">
          <span>API key</span>
          <PasswordInput v-model="key" autocomplete="off" placeholder="hv_…" :has-toggle="false" :is-required="true" />
        </label>
        <Button type="submit" class="w-full" :is-loading="pending">Connect</Button>
      </form>
      <p v-if="!connectionManaged" class="mt-4 text-xs text-muted-foreground">
        {{
          hasConnectionStorage
            ? 'This local tab reconnects on refresh. Disconnect clears the key.'
            : 'The key stays in memory until you disconnect or leave this page.'
        }}
      </p>
    </template>
  </Card>
</template>
