import { ref, shallowRef } from 'vue';
import { createDemoHarvestClient } from '../../integration/demo/CreateDemoHarvestClient';

/** Owns the optional synthetic client independently of the shared presentation package. */
export function useDemoConsole() {
  const demo = import.meta.env.MODE === 'demo';
  const session = ref(0);
  const client = shallowRef(demo ? createDemoHarvestClient() : undefined);
  function reset() {
    client.value = createDemoHarvestClient();
    session.value++;
  }
  return { demo, session, client, reset };
}
