import { DOMWrapper, flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, shallowRef } from 'vue';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import PipelineStageSettings from '../src/admin/harvestConsole/PipelineStageSettings.vue';
import SourceSettingsTab from '../src/admin/harvestConsole/SourceSettingsTab.vue';
import type { PipelineStageModel, PipelineStageOptions, SourceOverviewModel, SourceUpdate } from '../src/contracts';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', () => ({ useHarvestConsole: () => session }));

const wrappers: VueWrapper[] = [];
const aiAllowed = ref<boolean>();
const hasSourceScope = ref(true);
const saveStage = vi.fn(async () => ({ ok: true, value: true }));
const source: SourceOverviewModel = {
  sourceId: 'olx',
  name: 'OLX Uzbekistan',
  handle: 'www.olx.uz',
  family: 'site',
  state: 'healthy',
  sourceState: 'active',
  newPostsDaily: [],
  sections: [],
  health: { hostKey: 'www.olx.uz', minDelayMs: 5000, consecutiveFailures: 0 },
};

beforeEach(() => {
  aiAllowed.value = false;
  hasSourceScope.value = true;
  saveStage.mockClear();
  session = {
    client: shallowRef({ pipelineStages: { save: saveStage } }),
    canOperate: ref(true),
    canWrite: (scope: string) => scope === 'sources:manage' && hasSourceScope.value,
    pending: ref(false),
    groups: ref([]),
    capabilities: shallowRef({}),
    capabilityErrors: shallowRef({}),
    pipelines: ref([]),
    sectionsOf: () => [],
    canCapture: () => false,
    sourceOf: () => ({ aiAllowed: aiAllowed.value, state: 'active' }),
    updateSource: vi.fn(async (_sourceId: string, update: SourceUpdate) => {
      aiAllowed.value = update.aiAllowed;
      return true;
    }),
  } as unknown as HarvestConsole;
});

async function openProcessSettings(
  options: PipelineStageOptions,
  processorLabels: Readonly<Record<string, string>> = { 'rental-terms@2': 'Extract OLX rental terms' },
) {
  const stage: PipelineStageModel = {
    kind: 'process',
    available: true,
    options,
    runs: [],
    processorKeys: ['saved-reading@1', 'rental-terms@1', 'rental-terms@2'],
    processorLabels,
  };
  const wrapper = mount(PipelineStageSettings, {
    props: { pipelineId: 'olx-rentals', stage },
    attachTo: document.body,
  });
  wrappers.push(wrapper);
  await wrapper.get('button[aria-label="Schedule for Process"]').trigger('click');
  const body = new DOMWrapper(document.body);
  return vi.waitFor(() => body.get('[role="dialog"]'));
}

describe('stage model processing settings', () => {
  it('validates the model route and pins an integer prompt version when saving OLX rental terms', async () => {
    const dialog = await openProcessSettings({ batchSize: 10, processor: 'rental-terms@2' });
    expect(dialog.text()).toContain('Extract OLX rental terms');
    const form = dialog.get('form');
    const model = dialog.get('input[placeholder="provider/model"]');
    const prompt = dialog.get('input[type="number"][min="1"]');

    await model.setValue('missing-provider');
    await form.trigger('submit');
    expect(dialog.get('[role="alert"]').text()).toContain('provider/model');
    expect(saveStage).not.toHaveBeenCalled();

    await model.setValue('groq/openai/gpt-oss-20b');
    await prompt.setValue('1.5');
    await form.trigger('submit');
    expect(dialog.get('[role="alert"]').text()).toContain('whole prompt version');
    expect(saveStage).not.toHaveBeenCalled();

    await prompt.setValue('2');
    await form.trigger('submit');
    await flushPromises();
    expect(saveStage).toHaveBeenCalledExactlyOnceWith(
      'olx-rentals',
      expect.objectContaining({
        enabled: false,
        stage: {
          batchSize: 10,
          processor: 'rental-terms@2',
          model: 'groq/openai/gpt-oss-20b',
          promptVersion: 2,
        },
      }),
    );
    expect(session.updateSource).not.toHaveBeenCalled();
  });

  it('names a processor by the label the server sends, never by one the console knows', async () => {
    const dialog = await openProcessSettings(
      { batchSize: 10, processor: 'saved-reading@1' },
      { 'saved-reading@1': 'Read the saved capture' },
    );
    expect(dialog.text()).toContain('Read the saved capture');
    expect(dialog.text()).not.toContain('Saved reading');
  });

  it('shows the key of a processor the server sends no label for', async () => {
    const dialog = await openProcessSettings({ batchSize: 10, processor: 'rental-terms@1' }, {});
    expect(dialog.text()).toContain('rental-terms@1');
    expect(dialog.text()).not.toContain('Rental terms');
  });

  it('omits model and prompt settings for the saved-reading processor', async () => {
    const dialog = await openProcessSettings({
      batchSize: 30,
      processor: 'saved-reading@1',
      model: 'groq/openai/gpt-oss-20b',
      promptVersion: 2,
    });
    expect(dialog.text()).not.toContain('Prompt version');
    expect(dialog.find('input[placeholder="provider/model"]').exists()).toBe(false);

    await dialog.get('form').trigger('submit');
    await flushPromises();
    expect(saveStage).toHaveBeenCalledExactlyOnceWith(
      'olx-rentals',
      expect.objectContaining({ stage: { batchSize: 30, processor: 'saved-reading@1' } }),
    );
  });
});

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

function mountSourceSettings() {
  const wrapper = mount(SourceSettingsTab, { props: { source } });
  wrappers.push(wrapper);
  return wrapper;
}

const modelSwitch = '[role="switch"][aria-label="Allow model processing for OLX Uzbekistan"]';

describe('source model processing settings', () => {
  it('saves opt-in and opt-out independently of source and section schedules', async () => {
    const settings = mountSourceSettings();
    const control = settings.get<HTMLInputElement>(modelSwitch);
    expect(control.element.checked).toBe(false);

    await control.setValue(true);
    expect(session.updateSource).toHaveBeenNthCalledWith(1, 'olx', { aiAllowed: true });
    expect(control.element.checked).toBe(true);

    await control.setValue(false);
    expect(session.updateSource).toHaveBeenNthCalledWith(2, 'olx', { aiAllowed: false });
    expect(control.element.checked).toBe(false);
  });

  it('waits for a known setting and requires source management access', () => {
    aiAllowed.value = undefined;
    const settings = mountSourceSettings();
    expect(settings.get<HTMLInputElement>(modelSwitch).element.disabled).toBe(true);
    expect(session.updateSource).not.toHaveBeenCalled();

    hasSourceScope.value = false;
    expect(mountSourceSettings().find(modelSwitch).exists()).toBe(false);
  });
});
