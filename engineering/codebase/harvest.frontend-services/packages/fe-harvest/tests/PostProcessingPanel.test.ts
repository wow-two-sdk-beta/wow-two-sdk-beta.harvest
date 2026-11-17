import { mount, flushPromises } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { HarvestConsole } from '../src/admin/harvestConsole/HarvestConsole';
import type { PostProcessingModel } from '../src/contracts';
import PostProcessingPanel from '../src/admin/harvestConsole/PostProcessingPanel.vue';

let session: HarvestConsole;
vi.mock('../src/admin/harvestConsole/HarvestConsole', () => ({ useHarvestConsole: () => session }));
const wrappers: ReturnType<typeof mount>[] = [];
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});
function show() {
  const wrapper = mount(PostProcessingPanel, {
    props: { sourceId: 'olx', postId: 'post' },
    global: { stubs: { ConsoleLink: { template: '<a><slot /></a>' } } },
  });
  wrappers.push(wrapper);
  return wrapper;
}
const row: PostProcessingModel = {
  captureId: 'capture',
  sourceId: 'olx',
  linkId: 'link',
  runId: 'run',
  processor: 'rental-terms@2:anthropic/test:prompt-3',
  isCurrent: false,
  runStatus: 'completed',
  output: {
    schema: 'rental-terms@2',
    fields: [
      { key: 'deposit', label: 'Deposit', status: 'absent', claims: [{ evidence: 'Без залога.', value: null }] },
      { key: 'minimumStay', label: 'Minimum stay', status: 'unknown', claims: [] },
      {
        key: 'advanceRent',
        label: 'Advance rent',
        status: 'present',
        claims: [{ evidence: 'Предоплата за 3 месяца.', value: { duration: { count: 3, unit: 'month' } } }],
      },
    ],
    inferences: [{ promptVersion: 3, promptHash: 'hash', priced: false }],
  },
};

describe('processing evidence', () => {
  it('shows processor-owned fields, distinguishes absent from unknown, and scopes results to the source', async () => {
    session = {
      readPostProcessing: vi.fn(async () =>
        ResultExtensions.ok([row, { ...row, sourceId: 'private-source', processor: 'do-not-display' }]),
      ),
    } as unknown as HarvestConsole;
    const panel = show();
    await flushPromises();
    expect(panel.text()).toContain('Older capture');
    expect(panel.get('[aria-label="Deposit"]').text()).toContain('Explicitly absent');
    expect(panel.get('[aria-label="Deposit"]').text()).toContain('Без залога.');
    expect(panel.get('[aria-label="Minimum stay"]').text()).toContain('Not determined');
    expect(panel.get('[aria-label="Advance rent"]').text()).toContain('duration · count:3');
    expect(panel.get('[aria-label="Advance rent"]').text()).toContain('duration · unit:month');
    expect(panel.text()).not.toContain('do-not-display');
    expect(panel.get('pre').text()).toContain('"promptVersion": 3');
    expect(session.readPostProcessing).toHaveBeenCalledWith('olx', 'post', expect.any(AbortSignal));
  });

  it('keeps an unavailable response distinct from an empty result and supports refresh', async () => {
    const read = vi
      .fn()
      .mockResolvedValueOnce(ResultExtensions.fail({ kind: 'offline' }))
      .mockResolvedValueOnce(ResultExtensions.ok([]));
    session = { readPostProcessing: read } as unknown as HarvestConsole;
    const panel = show();
    await flushPromises();
    expect(panel.get('[role="alert"]').text()).toContain('could not be read');
    expect(panel.text()).not.toContain('No processing results');
    await panel.get('button').trigger('click');
    await flushPromises();
    expect(panel.find('[role="alert"]').exists()).toBe(false);
    expect(panel.text()).toContain('No processing results for this source yet');
  });
});
