import { onScopeDispose, shallowRef } from 'vue';
import { useRunningVersion } from '@wow-two-beta/fe/foundation/net';
import type { SystemStatus } from '../../domain/system/SystemStatus';
import { systemApi } from '../../integration/system';

/**
 * Follows the backend's status for this component's lifetime: read on mount, every five minutes while the tab is
 * visible, and at once when it returns. The version it reports is the release the host serves now, which the frame's
 * version badge compares with the one this tab loaded.
 */
export function useSystemStatus(enabled = true) {
  const controller = new AbortController();
  const status = shallowRef<SystemStatus | null>(null);
  const { version } = useRunningVersion(async () => {
    if (!enabled) return null;
    const result = await systemApi.getStatus(controller.signal);
    if (controller.signal.aborted || !result.ok) return null;
    status.value = result.value;
    return result.value.version;
  });
  onScopeDispose(() => controller.abort());

  return { status, runningVersion: version };
}
