import { DOMWrapper, flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, shallowRef } from 'vue';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import PipelineStageSettings from '../src/admin/harvestConsole/PipelineStageSettings.vue';
import ChoicePicker from '../src/admin/harvestConsole/ChoicePicker.vue';
import SourceSettingsTab from '../src/admin/harvestConsole/SourceSettingsTab.vue';
import type {
  ModelProcessingMode,
  PipelineStageModel,
  PipelineStageOptions,
  SourceOverviewModel,
  SourceUpdate,
} from '../src/contracts';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', () => ({ useHarvestConsole: () => session }));

const wrappers: VueWrapper[] = [];
const aiAllowed = ref<boolean>();
const modelProcessing = ref<ModelProcessingMode>();
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
  modelProcessing.value = undefined;
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
    sourceOf: () => ({ aiAllowed: aiAllowed.value, modelProcessing: modelProcessing.value, state: 'active' }),
    updateSource: vi.fn(async (_sourceId: string, update: SourceUpdate) => {
      modelProcessing.value = update.modelProcessing;
      aiAllowed.value = update.modelProcessing === undefined ? undefined : update.modelProcessing !== 'off';
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

const modelPicker = '[aria-label="Model processing for OLX Uzbekistan"]';

function picker(settings: VueWrapper) {
  return settings
    .findAllComponents(ChoicePicker)
    .find((candidate) => (candidate.props() as { label: string }).label === 'Model processing for OLX Uzbekistan')!;
}

function shown(settings: VueWrapper) {
  return picker(settings).props() as { modelValue: string; isDisabled: boolean };
}

describe('source model processing settings', () => {
  it('saves off, manual and automatic independently of source and section schedules', async () => {
    modelProcessing.value = 'off';
    const settings = mountSourceSettings();
    expect(shown(settings).modelValue).toBe('off');
    expect(settings.text()).toContain('Nothing from this source is sent to a model');

    picker(settings).vm.$emit('update:modelValue', 'manual');
    await flushPromises();
    expect(session.updateSource).toHaveBeenNthCalledWith(1, 'olx', { modelProcessing: 'manual' });
    expect(shown(settings).modelValue).toBe('manual');
    expect(settings.text()).toContain('operator runs that name its posts or list this source');

    picker(settings).vm.$emit('update:modelValue', 'auto');
    await flushPromises();
    expect(session.updateSource).toHaveBeenNthCalledWith(2, 'olx', { modelProcessing: 'auto' });
    expect(shown(settings).modelValue).toBe('auto');
  });

  it('reads an older host that reports only aiAllowed as automatic or off', () => {
    aiAllowed.value = true;
    expect(shown(mountSourceSettings()).modelValue).toBe('auto');
    aiAllowed.value = false;
    expect(shown(mountSourceSettings()).modelValue).toBe('off');
  });

  it('waits for a known setting and requires source management access', () => {
    aiAllowed.value = undefined;
    const settings = mountSourceSettings();
    expect(shown(settings).isDisabled).toBe(true);
    expect(session.updateSource).not.toHaveBeenCalled();

    hasSourceScope.value = false;
    expect(mountSourceSettings().find(modelPicker).exists()).toBe(false);
  });
});
