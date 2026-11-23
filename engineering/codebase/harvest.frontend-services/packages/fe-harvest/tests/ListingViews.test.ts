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

  it('links the listing place to both maps on the card and the page, and shows none without a place', () => {
    const card = mount(ListingCardFacts, { props: { listing: { rooms: 2, city: 'Tashkent', district: 'Chilonzor' } } });
    const links = card.findAll('a[data-map-provider]');
    expect(links).toHaveLength(2);
    for (const link of links) expect(link.attributes('href')).toContain(encodeURIComponent('Chilonzor · Tashkent'));
    expect(
      mount(ListingCardFacts, { props: { listing: { rooms: 2 } } })
        .find('[data-map-provider]')
        .exists(),
    ).toBe(false);
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
  it('sizes the lead column from portrait, square and landscape images', async () => {
    const gallery = mount(PhotoGallery, { props: { images: ['/lead.jpg', '/second.jpg', '/third.jpg'] } });
    try {
      const lead = gallery.get('button[aria-label="Open photo 1"] img');
      const mosaic = gallery.get<HTMLElement>('[style*="--mosaic-columns"]');
      for (const [width, height, aspect] of [
        [800, 1000, 0.8],
        [800, 800, 1],
        [1200, 800, 1.5],
      ]) {
        Object.defineProperties(lead.element, {
          naturalWidth: { value: width, configurable: true },
          naturalHeight: { value: height, configurable: true },
        });
        await lead.trigger('load');
        expect(mosaic.element.style.getPropertyValue('--mosaic-columns')).toBe(
          `minmax(0, ${aspect}fr) minmax(0, 0.4fr)`,
        );
      }
    } finally {
      gallery.unmount();
    }
  });

  it('shows whole previews and opens the overflow tile at its photo', async () => {
    const images = Array.from({ length: 6 }, (_, index) => `/test/photos/${index + 1}.png`);
    const gallery = mount(PhotoGallery, { props: { images }, attachTo: document.body });
    try {
      expect(gallery.findAll('button')).toHaveLength(5);
      expect(gallery.findAll('img').every((image) => image.classes().includes('object-contain'))).toBe(true);
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
