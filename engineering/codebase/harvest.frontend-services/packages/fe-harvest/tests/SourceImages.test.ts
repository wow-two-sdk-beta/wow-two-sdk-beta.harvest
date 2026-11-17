import { flushPromises, mount } from '@vue/test-utils';
import { defineComponent, h, ref, shallowRef, type ShallowRef, type VNode } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createHarvestClient, type HarvestClient } from '../src/kit';
import { provideHarvestConsole, type HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import PhotoGallery from '../src/admin/harvestConsole/PhotoGallery.vue';
import SourceImage from '../src/admin/harvestConsole/SourceImage.vue';

const Source = '11111111-1111-1111-1111-111111111111';
const Asset = '22222222-2222-2222-2222-222222222222';
const Path = `/api/sources/${Source}/assets/${Asset}`;
const OtherPath = `/api/sources/33333333-3333-3333-3333-333333333333/assets/${Asset}`;
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

  it('uses private blob URLs for thumbnails and lightbox, never forwarding credentials to public images', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(
      async () => new Response('image bytes', { headers: { 'Content-Type': 'image/jpeg' } }),
    );
    const client = shallowRef<HarvestClient | null>(createHarvestClient({ apiKey: 'hv_fixture', fetch }));
    const images = ref([Path, PublicImage]);
    const view = mount(
      withClient(client, () => h(PhotoGallery, { images: images.value })),
      { attachTo: document.body },
    );
    try {
      expect(view.findAll('img').map((image) => image.attributes('src'))).not.toContain(Path);
      await flushPromises();
      expect(fetch).toHaveBeenCalledTimes(1);
      expect(fetch.mock.calls[0]?.[0]).toBe(Path);
      expect(view.findAll('img').map((image) => image.attributes('src'))).toEqual([
        'blob:private-image-1',
        PublicImage,
      ]);
      await view.get('button[aria-label="Open photo 1"]').trigger('click');
      await vi.waitFor(() =>
        expect(document.body.querySelector('[role="dialog"] img')?.getAttribute('src')).toBe('blob:private-image-1'),
      );
      images.value = [OtherPath];
      await flushPromises();
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:private-image-1');
      expect(fetch.mock.calls[1]?.[0]).toBe(OtherPath);
      client.value = null;
      await flushPromises();
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:private-image-2');
      expect(view.findAll('img')).toHaveLength(0);
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
