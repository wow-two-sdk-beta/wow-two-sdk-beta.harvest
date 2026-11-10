<script setup lang="ts">
import { Badge, BadgeVariant, Card } from '@wow-two-beta/fe/presentation/display';
import { formatDate } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/** Each source host lane: its queue, running work, failures and any pause a challenge caused. */
defineOptions({ name: 'HostsPanel' });
const { hosts } = useHarvestConsole();

function isWaiting(pausedUntilUtc?: string) {
  return !!pausedUntilUtc && new Date(pausedUntilUtc) > new Date();
}
</script>

<template>
  <Card class="p-6">
    <p class="mb-5 text-sm text-muted-foreground">Queue depth and pauses update every five seconds.</p>
    <ul v-if="hosts.length" class="divide-y divide-border" aria-label="Host health">
      <li v-for="host in hosts" :key="`${host.hostKey}:${host.lane}`" class="space-y-2 py-4 first:pt-0 last:pb-0">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <p class="font-medium">
            {{ host.hostKey }}
            <span class="text-xs font-normal text-muted-foreground">· {{ host.lane }} lane</span>
          </p>
          <Badge :variant="isWaiting(host.pausedUntilUtc) ? BadgeVariant.Warning : BadgeVariant.Success">
            {{ isWaiting(host.pausedUntilUtc) ? 'Waiting' : 'Ready' }}
          </Badge>
        </div>
        <p class="text-sm text-muted-foreground">
          {{ host.queued }} queued · {{ host.running }} running · {{ host.consecutiveFailures }} consecutive failures
        </p>
        <p v-if="host.pausedUntilUtc" class="text-xs text-muted-foreground">
          Paused until {{ formatDate(host.pausedUntilUtc) }} {{ host.lastChallengeKind }}
        </p>
      </li>
    </ul>
    <p v-else class="text-sm text-muted-foreground">Host lanes appear when work is queued.</p>
  </Card>
</template>
