import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, shallowRef, type ShallowRef, type VNode } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createHarvestClient, type HarvestClient } from '../src/kit';
import { provideHarvestConsole, type HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import PhotoGallery from '../src/admin/harvestConsole/PhotoGallery.vue';
import SourceImage from '../src/admin/harvestConsole/SourceImage.vue';

const Source = '11111111-1111-1111-1111-111111111111';
const Asset = '22222222-2222-2222-2222-222222222222';
const Path = `/api/sources/${Source}/assets/${Asset}`;
const PublicImage = 'https://photos.example.test/public.jpg';
const createObjectURL = vi.fn<(blob: Blob) => string>();
const revokeObjectURL = vi.fn<(url: string) => void>();

function withClient(client: ShallowRef<HarvestClient | null>, render: () => VNode) {
  return defineComponent({
    setup() {
      provideHarvestConsole({ client } as HarvestConsole);
      return render;
    },
  });
}

beforeEach(() => {
  let index = 0;
  createObjectURL.mockReset().mockImplementation(() => `blob:private-image-${++index}`);
  revokeObjectURL.mockReset();
  vi.stubGlobal(
    'URL',
    class extends URL {
      static override createObjectURL = createObjectURL;
      static override revokeObjectURL = revokeObjectURL;
    },
  );
});
afterEach(() => vi.unstubAllGlobals());

describe('authenticated source media', () => {
  it('reads binary images through the existing key and cookie-capable API client', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(
      async () => new Response('image bytes', { headers: { 'Content-Type': 'image/png' } }),
    );
    const client = createHarvestClient({ apiKey: 'hv_fixture', credentials: 'same-origin', fetch });
    const result = await client.getSourceAsset!(Source, Asset);
    expect(result.ok).toBe(true);
    if (result.ok) expect(await result.value.text()).toBe('image bytes');
    expect(fetch.mock.calls[0]?.[0]).toBe(Path);
    expect(new Headers(fetch.mock.calls[0]?.[1]?.headers).get('X-Api-Key')).toBe('hv_fixture');
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBe('same-origin');
    expect(String(fetch.mock.calls[0]?.[0])).not.toContain('hv_fixture');
  });

  it('loads preview presets, fetches only the selected original and keeps public images on their original URLs', async () => {
    const paths = Array.from({ length: 6 }, (_, index) => Path.slice(0, -1) + (index + 1));
    const imageResponse = () => new Response('image bytes', { headers: { 'Content-Type': 'image/jpeg' } });
    let finishOriginal!: (response: Response) => void;
    const fetch = vi.fn<typeof globalThis.fetch>(async (url) =>
      url === paths[4] ? new Promise((resolve) => (finishOriginal = resolve)) : imageResponse(),
    );
    const requested = () => fetch.mock.calls.map(([url]) => String(url));
    const client = shallowRef<HarvestClient | null>(createHarvestClient({ apiKey: 'hv_fixture', fetch }));
    const images = [...paths, PublicImage];
    const view = mount(
      withClient(client, () => h(PhotoGallery, { images })),
      { attachTo: document.body },
    );
    try {
      await flushPromises();
      const previews = [`${paths[0]}?preset=s3`, ...paths.slice(1, 5).map((path) => `${path}?preset=s2`)];
      expect(requested()).toEqual(previews);
      const overflow = view.get('button[aria-label="View all 7 photos"]');
      const lastPreview = overflow.get('img').attributes('src');
      expect(lastPreview).toMatch(/^blob:/);
      await overflow.trigger('click');
      const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')!;
      const main = () => dialog().querySelector('figure img')!;
      await vi.waitFor(() => expect(main().getAttribute('src')).toBe(lastPreview));
      await flushPromises();
      const rail = Array.from(dialog().querySelectorAll('[role="group"] img'), (image) => image.getAttribute('src'));
      expect(rail.slice(0, 6).every((src) => src?.startsWith('blob:'))).toBe(true);
      expect(rail[4]).not.toBe(lastPreview);
      expect(rail[6]).toBe(PublicImage);
      const thumbnailRequests = paths.map((path) => `${path}?preset=s1`);
      expect(requested().slice(5).sort()).toEqual([...thumbnailRequests, paths[4]].sort());
      const pending = fetch.mock.calls.find(([url]) => url === paths[4])?.[1]?.signal;

      dialog().querySelector<HTMLButtonElement>('button[aria-label="Show image 6"]')!.click();
      await vi.waitFor(() => {
        expect(main().getAttribute('alt')).toBe('Photo 6');
        expect(main().getAttribute('src')).toMatch(/^blob:/);
        expect(main().getAttribute('src')).not.toBe(rail[5]);
      });
      expect(requested().at(-1)).toBe(paths[5]);
      expect(requested().filter((url) => !url.includes('?'))).toEqual(paths.slice(4));
      const selected = main().getAttribute('src');
      expect(pending?.aborted).toBe(true);
      finishOriginal(imageResponse());
      await flushPromises();
      expect(main().getAttribute('src')).toBe(selected);
    } finally {
      view.unmount();
    }
  });

  it('revokes loaded images on unmount and cancels a pending response without exposing it', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(
      async () => new Response('image bytes', { headers: { 'Content-Type': 'image/png' } }),
    );
    const client = shallowRef<HarvestClient | null>(createHarvestClient({ fetch }));
    const host = () => withClient(client, () => h(SourceImage, { src: Path, alt: 'Private photo' }));
    const loaded = mount(host());
    await flushPromises();
    expect(loaded.get('img').attributes('src')).toBe('blob:private-image-1');
    loaded.unmount();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:private-image-1');
    let finish!: (value: Response) => void;
    fetch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const pending = mount(host());
    await flushPromises();
    const signal = fetch.mock.calls[1]?.[1]?.signal;
    pending.unmount();
    expect(signal?.aborted).toBe(true);
    finish(new Response('late image', { headers: { 'Content-Type': 'image/png' } }));
    await flushPromises();
    expect(createObjectURL).toHaveBeenCalledTimes(1);
  });

  it('keeps private failures out of img requests and supports clients without the optional image method', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response('unauthorized', { status: 401 }));
    const client = shallowRef<HarvestClient | null>(createHarvestClient({ fetch }));
    const view = mount(withClient(client, () => h(SourceImage, { src: Path })));
    try {
      await flushPromises();
      expect(view.find('img').exists()).toBe(false);
      expect(view.getComponent(SourceImage).emitted('error')).toHaveLength(1);
      const legacy = { ...client.value! };
      delete legacy.getSourceAsset;
      client.value = legacy;
      await flushPromises();
      expect(fetch).toHaveBeenCalledTimes(1);
      expect(createObjectURL).not.toHaveBeenCalled();
    } finally {
      view.unmount();
    }
  });
});
