import { computed, inject, onScopeDispose, ref, shallowRef, watch, type InjectionKey } from 'vue';
import {
  BffSessionExtensions,
  createAuthBridge,
  createBffStrategy,
  useBffAuth,
  type BffUser,
} from '@wow-two-beta/fe/auth';
import { ApiFailureFactory, createApiClient } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { createHarvestClient, type HarvestClient } from '@wow-two-beta/fe-harvest/kit';

interface ConsoleAccess {
  readonly identityEnabled: boolean;
  readonly hasAccess: boolean;
  readonly isOperator: boolean;
  readonly tenantId?: string;
  readonly scopes: readonly string[];
}

/** Binds the shared BFF strategy to Harvest's explicit authentication mode and tenant permissions. */
export function createHarvestIdentity(enabled = true) {
  const identityEnabled = ref(false);
  const access = shallowRef<ConsoleAccess>();
  const message = ref<string | null>(null);
  const bridge = createAuthBridge<BffUser>({ getIdentity: BffSessionExtensions.identity });
  const bff = createBffStrategy({ sync: { channelName: 'harvest.identity' } });
  const api = createApiClient({ credentials: 'same-origin' });
  bridge.subscribeExpired(() => {
    message.value = 'Your session has ended. Sign in again.';
  });
  let configured = false;
  let accessKey = '';
  async function readAccess(signal?: AbortSignal) {
    const outcome = await api.get('/api/console/access', {
      ...(signal ? { signal } : {}),
      decode: (value) => {
        const data = value as Partial<ConsoleAccess> | null;
        return data &&
          typeof data.identityEnabled === 'boolean' &&
          (data.identityEnabled === false ||
            (typeof data.hasAccess === 'boolean' &&
              typeof data.isOperator === 'boolean' &&
              Array.isArray(data.scopes) &&
              data.scopes.every((scope) => typeof scope === 'string')))
          ? ResultExtensions.ok(data as ConsoleAccess)
          : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
      },
    });
    if (outcome.ok) {
      identityEnabled.value = outcome.value.identityEnabled;
      const key = JSON.stringify([outcome.value.tenantId, outcome.value.isOperator, outcome.value.scopes]);
      if (accessKey && accessKey !== key) bridge.scope.invalidate();
      accessKey = key;
      access.value = outcome.value;
      configured = true;
    }
    return outcome;
  }
  const strategy: typeof bff = {
    ...bff,
    connect: (host) => (enabled ? (bff.connect?.(host) ?? (() => {})) : () => {}),
    async resolveUser(context) {
      message.value = null;
      if (!configured) {
        const mode = await readAccess(context.signal);
        if (!mode.ok) return mode;
      }
      if (!identityEnabled.value) return ResultExtensions.ok(null);
      const session = await bff.resolveUser(context);
      if (!session.ok || session.value === null) return session;
      const permissions = await readAccess(context.signal);
      return permissions.ok ? session : permissions;
    },
  };
  function resolveError() {
    message.value = 'SCFD could not verify your session. Try again.';
  }
  return { identityEnabled, access, message, bridge, bff, strategy, resolveError };
}

export const HarvestIdentityKey: InjectionKey<ReturnType<typeof createHarvestIdentity>> = Symbol('harvest.identity');

/** Composes the shared Identity session and request lifetime with Harvest's authorized console client. */
export function useHarvestIdentity() {
  const provided = inject(HarvestIdentityKey);
  if (!provided) throw new Error('Harvest Identity requires its app provider.');
  const runtime = provided;
  const auth = useBffAuth();
  const epoch = ref(0);
  const signingOut = ref(false);
  const client = shallowRef<HarvestClient>();
  const dispose = runtime.bridge.scope.subscribe(() => {
    epoch.value++;
  });
  onScopeDispose(dispose);
  watch(
    () => [auth.isAuthenticated, runtime.access.value] as const,
    () => {
      client.value =
        auth.isAuthenticated && runtime.access.value?.hasAccess
          ? createHarvestClient({
              credentials: 'same-origin',
              scope: runtime.bridge.scope,
              onUnauthorized: runtime.bridge.onUnauthorized,
              antiforgery: runtime.bff.antiforgery,
            })
          : undefined;
    },
    { immediate: true },
  );
  const message = computed(
    () =>
      runtime.message.value ??
      (auth.isAuthenticated && !runtime.access.value?.hasAccess
        ? 'Your account has no access to this SCFD workspace. Contact your organization administrator.'
        : null),
  );
  async function signOut() {
    if (signingOut.value) return;
    signingOut.value = true;
    try {
      const result = await auth.signOut();
      if (!result.ok) runtime.message.value = 'Sign-out could not finish. Try again.';
    } finally {
      signingOut.value = false;
    }
  }
  return {
    ready: computed(() => !auth.isPending && runtime.access.value !== undefined),
    pending: computed(() => auth.isPending || signingOut.value),
    identityEnabled: runtime.identityEnabled,
    isOperator: computed(() => runtime.access.value?.isOperator ?? false),
    signedIn: computed(() => auth.isAuthenticated),
    accountName: computed(() => auth.person?.name ?? 'Signed in'),
    message,
    permissions: computed(() => (auth.isAuthenticated ? (runtime.access.value?.scopes ?? []) : [])),
    client,
    epoch,
    load: () => auth.refresh(),
    signOut,
  };
}
