import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, expect, it, vi } from 'vitest';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { createWebRouter } from '../src/bootstrap/router/Routes';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';

/** The demo's Afisha.uz Settings. */
const AfishaSettings = '/sources/00000000-0000-4000-8000-000000000200/settings';

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

it('opens the demo console without a key or backend and resets its state', async () => {
  vi.stubEnv('MODE', 'demo');
  const fetch = vi.fn(() => {
    throw new Error('The demo must not use the network');
  });
  vi.stubGlobal('fetch', fetch);
  const router = createWebRouter(RouterHistoryMode.Memory);
  await router.push(AfishaSettings);
  await router.isReady();
  const wrapper = mount(AppRoot, { global: { plugins: [router] }, attachTo: document.body });
  const state = () => wrapper.get('header[aria-label="Source"] h1').text();
  try {
    await vi.waitFor(() => expect(wrapper.text()).toContain('Afisha, from an adapter release'));
    expect(wrapper.get('[aria-label="Demo mode"]').text()).toContain('synthetic data');
    expect(state()).toContain('healthy');
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
    await wrapper.get('[aria-label="Reading"] input').trigger('click');
    await flushPromises();
    await vi.waitFor(() => expect(state()).toContain('idle'));
    const reset = wrapper.findAll('button').find((button) => button.text() === 'Reset demo')!;
    await reset.trigger('click');
    await vi.waitFor(() => expect(state()).toContain('healthy'));
    expect(fetch).not.toHaveBeenCalled();
  } finally {
    wrapper.unmount();
  }
});
