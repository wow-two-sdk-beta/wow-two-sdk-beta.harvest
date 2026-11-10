import type { Ref } from 'vue';
import { createPersistentRef } from '@wow-two-beta/fe/foundation/storage';

/** The `localStorage` key the sidebar's collapsed state lives under, beside `scfd.theme`. */
const SidebarCollapsedKey = 'scfd.sidebar-collapsed';

const sidebarCollapsed = createPersistentRef(SidebarCollapsedKey, false, {
  decode: (stored) => (typeof stored === 'boolean' ? stored : undefined),
});

/** Whether the desktop sidebar is collapsed to its icon rail, remembered across visits. */
export function useSidebarCollapsed(): Ref<boolean> {
  return sidebarCollapsed;
}
