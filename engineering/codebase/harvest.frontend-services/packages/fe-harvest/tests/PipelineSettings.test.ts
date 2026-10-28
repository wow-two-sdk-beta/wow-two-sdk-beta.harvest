import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import PipelineSettings from '../src/admin/harvestConsole/PipelineSettings.vue';
import type { PipelineModel, PipelineUpdate } from '../src/contracts';

function pipeline(): PipelineModel {
  return {
    id: 'saved-search',
    containerId: 'container',
    sourceId: 'source',
    scopeKey: 'scope',
    name: 'Saved search',
    scope: { url: 'https://olx.uz/search?q=sewing&currency=UZS', parameters: { city: 'tashkent' } },
    state: 'active',
    pinned: true,
    ai: 'off',
    limits: {},
    steps: {},
    createdAtUtc: '2026-10-27T10:00:00Z',
    schedules: [
      {
        id: 'capture-schedule',
        kind: 'capture',
        mode: 'cron',
        cron: '0 9 * * *',
        enabled: false,
        timeZone: 'Asia/Tashkent',
        window: { start: '08:00:00', end: '20:00:00' },
      },
    ],
  };
}

describe('Pipeline settings', () => {
  it('uses adapter URL guidance and prevents unsupported schedules without discarding existing ones', async () => {
    const original = pipeline();
    const wrapper = mount(PipelineSettings, {
      props: {
        pipeline: original,
        pending: false,
        capabilities: {
          sourceId: original.sourceId,
          workKinds: ['capture'],
          configuration: {
            displayName: 'Named source',
            readiness: 'ready',
            scopeSchema: { required: ['url'], properties: { url: { description: 'A saved publisher profile URL.' } } },
          },
        },
      },
    });
    try {
      expect(wrapper.text()).toContain('A saved publisher profile URL.');
      expect(wrapper.get('[aria-label="Enable metrics schedule"]').attributes('disabled')).toBeDefined();
      await wrapper.get('input[name="pipeline-url"]').setValue('');
      await wrapper.get('form').trigger('submit');
      expect(wrapper.text()).toContain('requires a starting URL');
      expect(wrapper.emitted('save')).toBeUndefined();
    } finally {
      wrapper.unmount();
    }
  });
  it('preserves a full filtered URL, overrides and disabled cron settings when saving', async () => {
    const wrapper = mount(PipelineSettings, { props: { pipeline: pipeline(), pending: false } });
    await wrapper
      .get('input[name="pipeline-url"]')
      .setValue('https://olx.uz/search?q=sewing&search%5Bfilter_float_price%3Ato%5D=500000');
    await wrapper.get('form').trigger('submit');
    const saved = wrapper.emitted('save')![0]![0] as PipelineUpdate;
    expect(saved.scope.url).toContain('search%5Bfilter_float_price%3Ato%5D=500000');
    expect(saved.scope.parameters).toEqual({ city: 'tashkent' });
    expect(saved.schedules).toEqual([
      {
        kind: 'capture',
        mode: 'cron',
        cron: '0 9 * * *',
        enabled: false,
        timeZone: 'Asia/Tashkent',
        window: { start: '08:00:00', end: '20:00:00' },
      },
    ]);
    expect(wrapper.text()).toContain('Manual only');
    wrapper.unmount();
  });

  it('keeps an unsaved draft during a background refresh and resets for another pipeline', async () => {
    const original = pipeline();
    const wrapper = mount(PipelineSettings, { props: { pipeline: original, pending: false } });
    await wrapper.get('input[name="pipeline-name"]').setValue('Unsaved draft');
    await wrapper.setProps({ pipeline: { ...original, name: 'Server refresh' } });
    expect((wrapper.get('input[name="pipeline-name"]').element as HTMLInputElement).value).toBe('Unsaved draft');
    await wrapper.setProps({ pipeline: { ...original, id: 'another', name: 'Another account' } });
    expect((wrapper.get('input[name="pipeline-name"]').element as HTMLInputElement).value).toBe('Another account');
    wrapper.unmount();
  });
});
