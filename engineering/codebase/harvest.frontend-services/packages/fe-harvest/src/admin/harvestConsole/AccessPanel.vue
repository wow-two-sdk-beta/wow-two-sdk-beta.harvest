<script setup lang="ts">
import { ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant, Card } from '@wow-two-beta/fe/presentation/display';
import { TextInput } from '@wow-two-beta/fe/presentation/forms';
import { formatDate } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/** Who may do what: this session's scopes, the tenants that consume SCFD and the containers they see. */
defineOptions({ name: 'AccessPanel' });
const session = useHarvestConsole();
const { tenants, containers, containerId, pending, can, isOperator, createPool, registerTenant } = session;
const tenantName = ref('');
/** The scopes a console session can hold, in the order the server documents them. @internal */
const Scopes = [
  'pipelines:read',
  'pipelines:run',
  'pipelines:manage',
  'posts:read',
  'sources:manage',
  'subscriptions:manage',
  'events:read',
  'tenants:manage',
  'containers:manage',
] as const;

async function addTenant() {
  if (await registerTenant(tenantName.value.trim())) tenantName.value = '';
}
</script>

<template>
  <div class="grid gap-6 xl:grid-cols-2">
    <Card class="self-start p-6">
      <h2 class="text-base font-semibold">This session</h2>
      <p class="mt-1 text-sm text-muted-foreground">
        {{
          isOperator === undefined
            ? 'The server checks every action against the connection’s scopes.'
            : isOperator
              ? 'You operate SCFD: every container is yours to change.'
              : 'You belong to a tenant: you change only its private container.'
        }}
      </p>
      <ul class="mt-5 flex flex-wrap gap-2" aria-label="Scopes">
        <li v-for="scope in Scopes" :key="scope">
          <Badge :variant="can(scope) ? BadgeVariant.Success : BadgeVariant.Neutral">
            {{ scope }}{{ can(scope) ? '' : ' · no' }}
          </Badge>
        </li>
      </ul>
    </Card>

    <Card class="self-start p-6">
      <h2 class="text-base font-semibold">Tenants</h2>
      <ul v-if="tenants.length" class="mt-4 divide-y divide-border" aria-label="Tenants">
        <li v-for="tenant in tenants" :key="tenant.id" class="flex items-center justify-between gap-3 py-3">
          <div class="min-w-0">
            <p class="truncate font-medium">{{ tenant.name }}</p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ tenant.slug }} · since {{ formatDate(tenant.createdAtUtc) }}
            </p>
          </div>
          <Badge :variant="tenant.state === 'active' ? BadgeVariant.Success : BadgeVariant.Warning">
            {{ tenant.state }}
          </Badge>
        </li>
      </ul>
      <p v-else class="mt-4 text-sm text-muted-foreground">No tenants yet.</p>
      <form
        v-if="can('tenants:manage')"
        class="mt-5 flex flex-wrap items-end gap-3 border-t border-border pt-5"
        aria-label="Register a tenant"
        @submit.prevent="addTenant"
      >
        <label class="block min-w-0 flex-1 space-y-2 text-sm">
          <span>New tenant name</span>
          <TextInput v-model="tenantName" placeholder="events" :is-required="true" />
        </label>
        <Button type="submit" variant="outline" :is-loading="pending">Register tenant</Button>
      </form>
    </Card>

    <Card class="self-start p-6 xl:col-span-2">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="text-base font-semibold">Containers</h2>
        <Button
          v-if="can('tenants:manage') && can('containers:manage') && !containers.some((item) => item.kind === 'pool')"
          size="sm"
          variant="outline"
          :is-loading="pending"
          @click="createPool"
        >
          Create shared container
        </Button>
      </div>
      <ul v-if="containers.length" class="mt-4 divide-y divide-border" aria-label="Containers">
        <li v-for="item in containers" :key="item.id" class="flex flex-wrap items-center justify-between gap-3 py-3">
          <div class="min-w-0">
            <p class="truncate font-medium">{{ item.externalRef ?? item.id }}</p>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ item.kind === 'pool' ? 'Shared pool' : 'Private' }}
              <template v-if="item.tenantId">
                · {{ tenants.find((tenant) => tenant.id === item.tenantId)?.name ?? item.tenantId }}
              </template>
            </p>
          </div>
          <Badge v-if="item.id === containerId" :variant="BadgeVariant.Brand">open</Badge>
        </li>
      </ul>
      <p v-else class="mt-4 text-sm text-muted-foreground">No containers yet.</p>
    </Card>
  </div>
</template>
