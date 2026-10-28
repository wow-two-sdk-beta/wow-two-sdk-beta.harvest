import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, expect, it, vi } from 'vitest';
import ConsolePage from '../src/presentation/console/pages/ConsolePage.vue';

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
  const wrapper = mount(ConsolePage);
  try {
    await flushPromises();
    expect(wrapper.get('[aria-label="Demo mode"]').text()).toContain('synthetic data');
    expect(wrapper.text()).toContain('Events · demo');
    expect(wrapper.text()).toContain('Synthetic source adapter');
    expect(wrapper.find('input[type="password"]').exists()).toBe(false);
    expect(wrapper.findAll('button').some((button) => button.text() === 'Disconnect')).toBe(false);
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Pause')!
      .trigger('click');
    await flushPromises();
    expect(wrapper.get('[aria-label="Pipeline overview"]').text()).toContain('paused');
    const reset = wrapper.findAll('button').find((button) => button.text() === 'Reset demo')!;
    await reset.trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('Events · demo');
    expect(wrapper.get('[aria-label="Pipeline overview"]').text()).toContain('active');
    expect(fetch).not.toHaveBeenCalled();
  } finally {
    wrapper.unmount();
  }
});
