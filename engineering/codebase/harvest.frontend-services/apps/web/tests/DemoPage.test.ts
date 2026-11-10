import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, expect, it, vi } from 'vitest';
import AppRoot from '../src/bootstrap/AppRoot.vue';
import { createWebRouter } from '../src/bootstrap/router/Routes';
import { RouterHistoryMode } from '@wow-two-beta/fe/router';

/** The first synthetic pipeline, `Events · demo`. */
const DemoPipeline = '/pipelines/00000000-0000-4000-8000-000000000005';

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
  await router.push(DemoPipeline);
  await router.isReady();
  const wrapper = mount(AppRoot, { global: { plugins: [router] } });
  try {
    await vi.waitFor(() => expect(wrapper.text()).toContain('Synthetic source adapter'));
    expect(wrapper.get('[aria-label="Demo mode"]').text()).toContain('synthetic data');
    expect(wrapper.get('h1').text()).toBe('Events · demo');
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
    await vi.waitFor(() => expect(wrapper.get('[aria-label="Pipeline overview"]').text()).toContain('active'));
    expect(wrapper.get('h1').text()).toBe('Events · demo');
    expect(fetch).not.toHaveBeenCalled();
  } finally {
    wrapper.unmount();
  }
});
