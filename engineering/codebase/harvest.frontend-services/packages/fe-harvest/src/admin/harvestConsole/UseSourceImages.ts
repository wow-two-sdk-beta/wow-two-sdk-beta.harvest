import { computed, inject, onScopeDispose, shallowRef, watch } from 'vue';
import type { SourceImagePreset } from '../../contracts/SourceImagePolicy';
import { HarvestConsoleKey } from './HarvestConsole';

const AssetPath = /^\/api\/sources\/([a-f0-9-]{36})\/assets\/([a-f0-9-]{36})$/i;
const ImageTypes = new Set([
  'image/png',
  'image/jpeg',
  'image/webp',
  'image/gif',
  'image/avif',
  'image/x-icon',
  'image/vnd.microsoft.icon',
]);

/** Resolves only private source-asset paths through the authenticated client. Public images keep their URLs.
 * Object URLs belong to this view and are revoked when its images, client or lifetime changes. */
export function useSourceImages(
  images: () => readonly string[],
  preset: () => SourceImagePreset | undefined = () => undefined,
) {
  const session = inject(HarvestConsoleKey, null);
  const copies = shallowRef<Readonly<Record<string, string>>>({});
  const failed = shallowRef<ReadonlySet<string>>(new Set());
  let request: AbortController | undefined;
  let owned: string[] = [];
  function clear() {
    request?.abort();
    for (const url of owned) URL.revokeObjectURL(url);
    owned = [];
    copies.value = {};
    failed.value = new Set();
  }
  watch(
    [() => images().join('\n'), () => session?.client.value, preset],
    async () => {
      clear();
      const controller = new AbortController();
      request = controller;
      const api = session?.client.value;
      const requested = preset();
      const privateUrls = [...new Set(images())].filter((url) => AssetPath.test(url));
      const results = await Promise.all(
        privateUrls.map(async (url) => {
          const match = AssetPath.exec(url)!;
          if (!api?.getSourceAsset) return { url };
          const result = requested
            ? await api.getSourceAsset(match[1]!, match[2]!, controller.signal, requested)
            : await api.getSourceAsset(match[1]!, match[2]!, controller.signal);
          return result.ok && result.value.size <= 5 * 1024 * 1024 && ImageTypes.has(result.value.type.toLowerCase())
            ? { url, blob: result.value }
            : { url };
        }),
      );
      if (controller.signal.aborted) return;
      const mapped: Record<string, string> = {};
      const unavailable = new Set<string>();
      for (const result of results) {
        if (!result.blob) {
          unavailable.add(result.url);
          continue;
        }
        const url = URL.createObjectURL(result.blob);
        owned.push(url);
        mapped[result.url] = url;
      }
      copies.value = mapped;
      failed.value = unavailable;
    },
    { immediate: true },
  );
  onScopeDispose(clear);
  function srcFor(url: string): string | undefined {
    return AssetPath.test(url) ? copies.value[url] : url;
  }

  return {
    urls: computed(() =>
      images()
        .map(srcFor)
        .filter((url): url is string => !!url),
    ),
    srcFor,
    failed,
  };
}
