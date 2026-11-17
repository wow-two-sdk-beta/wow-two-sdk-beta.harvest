import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import ListingCardFacts from '../src/admin/harvestConsole/ListingCardFacts.vue';
import ListingGallery from '../src/admin/harvestConsole/ListingGallery.vue';
import ListingPostFacts from '../src/admin/harvestConsole/ListingPostFacts.vue';
import PhotoGallery from '../src/admin/harvestConsole/PhotoGallery.vue';

describe('apartment listing views', () => {
  it('shows the asking price and both floors on a card and holds raw codes for review in details', () => {
    const card = mount(ListingCardFacts, {
      props: {
        listing: { rooms: 2, floor: 4, buildingFloors: 9, totalAreaM2: 58, price: { amount: 400, currency: 'USD' } },
        onImage: true,
      },
    });
    expect(card.text()).toContain('400 USD');
    expect(card.text()).toContain('2 rooms');
    expect(card.text()).toContain('58 m²');
    expect(card.text()).toContain('Floor 4 of 9');
    const detail = mount(ListingPostFacts, {
      props: {
        listing: {
          schema: 'wow2.listing.v1',
          property: { kind: 'apartment' },
          unit: { rooms: 2, floor: 4, buildingFloors: 9 },
          unknownValues: [
            { path: '/property/renovation', sourcePath: '/fields/detail/repair', raw: '4', reason: 'providerCode' },
          ],
        },
        description: 'Two rooms near the metro.',
      },
    });
    expect(detail.get('section[aria-label="Property"]').text()).toContain('Floor 4 of 9');
    expect(detail.get('section[aria-label="Description"]').text()).toContain('Two rooms');
    expect(detail.get('details').text()).toContain('Repair');
    expect(detail.find('details[open]').exists()).toBe(false);
  });

  it('keeps the chosen photo when a different thumbnail fails', async () => {
    const images = ['/test/photos/first.png', '/test/photos/second.png', '/test/photos/third.png'];
    const gallery = mount(ListingGallery, { props: { images, removed: false } });
    const main = () => gallery.get('img[alt^="Property photo"]');
    await gallery.get('button[aria-label="Show property photo 2"]').trigger('click');
    expect(main().attributes('src')).toBe(images[1]);
    await gallery.get('button[aria-label="Show property photo 1"] img').trigger('error');
    expect(main().attributes('src')).toBe(images[1]);
    expect(gallery.findAll('button')).toHaveLength(2);
  });
});

describe('captured post photos', () => {
  it('opens a tinted overflow tile at its photo and reaches the remaining photos', async () => {
    const images = Array.from({ length: 6 }, (_, index) => `/test/photos/${index + 1}.png`);
    const gallery = mount(PhotoGallery, { props: { images }, attachTo: document.body });
    try {
      expect(gallery.findAll('button')).toHaveLength(5);
      const last = gallery.get('button[aria-label="View all 6 photos"]');
      expect(last.text()).toBe('+1');
      await last.trigger('click');
      const dialog = () => document.body.querySelector('[role="dialog"]');
      await vi.waitFor(() => expect(dialog()?.querySelector('img')?.getAttribute('src')).toBe(images[4]));
      dialog()!.querySelector<HTMLButtonElement>('button[aria-label="Next image"]')!.click();
      await flushPromises();
      expect(dialog()?.querySelector('img')?.getAttribute('src')).toBe(images[5]);
    } finally {
      gallery.unmount();
    }
  });
});
