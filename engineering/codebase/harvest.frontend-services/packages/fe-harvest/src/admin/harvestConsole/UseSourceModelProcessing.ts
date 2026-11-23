import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import type { ModelProcessingMode } from '../../contracts';
import { useHarvestConsole } from './HarvestConsole';

export const ModelProcessingChoices: readonly { readonly value: ModelProcessingMode; readonly label: string }[] = [
  { value: 'off', label: 'Off' },
  { value: 'manual', label: 'Manual' },
  { value: 'auto', label: 'Automatic' },
];

/** Reads and updates the source policy shared by Settings and the Instagram Process card. */
export function useSourceModelProcessing(sourceId: MaybeRefOrGetter<string>) {
  const session = useHarvestConsole();
  const mode = computed<ModelProcessingMode | undefined>(() => {
    const source = session.sourceOf(toValue(sourceId));
    if (source?.modelProcessing) return source.modelProcessing;
    return source?.aiAllowed === true ? 'auto' : source?.aiAllowed === false ? 'off' : undefined;
  });
  const canManage = computed(() => session.canOperate.value && session.canWrite('sources:manage'));
  const canChange = computed(() => canManage.value && !session.pending.value && mode.value !== undefined);

  function setMode(value: ModelProcessingMode) {
    if (canChange.value && mode.value !== value)
      void session.updateSource(toValue(sourceId), { modelProcessing: value });
  }

  return { mode, canManage, canChange, setMode };
}
