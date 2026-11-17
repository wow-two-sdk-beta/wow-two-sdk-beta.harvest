import { computed, ref, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue';
import type { PipelineStageModel } from '../../contracts/PipelineStagesModel';
import { useHarvestConsole } from './HarvestConsole';

export const PipelinePollMs = 4000;

/** Shares the stage snapshot and refresh behavior between the source tab and the pipeline page. */
export function usePipelineStages(pipelineId: MaybeRefOrGetter<string>) {
  const session = useHarvestConsole();
  const client = computed(() => session.client.value?.pipelineStages);
  const stages = shallowRef<readonly PipelineStageModel[]>([]);
  const loading = ref(false);
  const error = ref(false);
  let refresh: () => void = () => {};

  watch(
    [client, () => toValue(pipelineId)],
    ([api, id], _, cancel) => {
      stages.value = [];
      loading.value = !!api && !!id;
      error.value = false;
      refresh = () => {};
      if (!api || !id) return;
      const controller = new AbortController();
      let reading = false;
      let refreshAfterRead = false;

      refresh = async () => {
        if (controller.signal.aborted) return;
        if (reading) {
          refreshAfterRead = true;
          return;
        }
        reading = true;
        try {
          const result = await api.list(id, controller.signal);
          if (controller.signal.aborted) return;
          if (result.ok) stages.value = result.value;
          error.value = !result.ok;
        } catch {
          if (!controller.signal.aborted) error.value = true;
        } finally {
          reading = false;
          if (!controller.signal.aborted) loading.value = false;
          if (refreshAfterRead && !controller.signal.aborted) {
            refreshAfterRead = false;
            refresh();
          }
        }
      };
      refresh();
      const timer = window.setInterval(() => {
        if (!reading && !document.hidden) refresh();
      }, PipelinePollMs);
      cancel(() => {
        controller.abort();
        window.clearInterval(timer);
      });
    },
    { immediate: true },
  );

  return { client, stages, loading, error, refresh: () => refresh() };
}
