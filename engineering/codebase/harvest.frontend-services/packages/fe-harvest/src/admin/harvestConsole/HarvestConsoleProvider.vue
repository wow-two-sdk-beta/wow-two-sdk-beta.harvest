<script setup lang="ts">
import type { HarvestClient } from '../../kit';
import {
  createHarvestConsole,
  provideHarvestConsole,
  type ConsoleNavigation,
  type HarvestConsole,
} from './HarvestConsole';

/**
 * Opens one console session and shares it with every SCFD panel inside: the connection, the open container, its data
 * on a five-second refresh, and the actions. Key it to start a fresh session.
 */
defineOptions({ name: 'HarvestConsoleProvider' });
const props = withDefaults(
  defineProps<{
    /** The connected client; without one the console waits for an API key or a sign-in. */
    client?: HarvestClient;
    /** The embedding app owns the connection: no key form and no disconnect. */
    connectionManaged?: boolean;
    /** Where an opted-in local tab keeps its key between reloads. */
    connectionStorage?: Storage;
    /** The signed-in scopes; omitted, every action shows and the server decides. */
    permissions?: readonly string[];
    /** Whether the signed-in account operates SCFD. */
    isOperator?: boolean;
    /** Whether this server runs a capture worker. */
    localWorkerEnabled?: boolean;
    /** Resolves and opens console places in the host app. */
    navigation?: ConsoleNavigation;
    /** How often the open container refreshes, in milliseconds. */
    refreshIntervalMs?: number;
  }>(),
  {
    client: undefined,
    connectionManaged: undefined,
    connectionStorage: undefined,
    permissions: undefined,
    isOperator: undefined,
    localWorkerEnabled: undefined,
    navigation: undefined,
    refreshIntervalMs: undefined,
  },
);
defineSlots<{ default(props: { session: HarvestConsole }): unknown }>();
const session = createHarvestConsole(props);
provideHarvestConsole(session);
</script>

<template>
  <slot :session="session"></slot>
</template>
