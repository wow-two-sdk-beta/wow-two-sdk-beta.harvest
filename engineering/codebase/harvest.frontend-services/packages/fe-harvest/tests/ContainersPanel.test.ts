import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ContainersPanel } from '../src/admin';
import { createRegistryClient } from '../src/kit';

const wrappers: ReturnType<typeof mount>[] = [];

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount();
});

async function render(...responses: (() => Response)[]) {
  const fetch = vi.fn<typeof globalThis.fetch>(async () => (responses.shift() ?? (() => Response.error()))());
  const wrapper = mount(ContainersPanel, {
    props: { client: createRegistryClient({ baseUrl: 'https://harvest.example.test', fetch }) },
  });
  wrappers.push(wrapper);
  await flushPromises();
  return { fetch, wrapper };
}

describe('containers panel', () => {
  it('lists the containers the key may see', async () => {
    const { wrapper } = await render(() =>
      Response.json({
        data: [
          { id: 'c-1', kind: 'pool', embeddingsEnabled: false, state: 'active', createdAtUtc: '2026-10-27T00:00:00Z' },
          {
            id: 'c-2',
            kind: 'private',
            tenantId: 't-1',
            externalRef: 'ocharo-fashion',
            embeddingsEnabled: false,
            state: 'active',
            createdAtUtc: '2026-10-27T00:00:00Z',
          },
        ],
      }),
    );

    const items = wrapper.get('[aria-label="Containers"]').findAll('li');
    expect(items).toHaveLength(2);
    expect(items[1]?.text()).toContain('ocharo-fashion');
  });

  it('says a key is needed when the service answers 401', async () => {
    const { wrapper } = await render(() => Response.json({ title: 'Unauthorized' }, { status: 401 }));

    expect(wrapper.get('[role="alert"]').text()).toBe('An API key is needed to read the registry.');
  });

  it('loads again on request', async () => {
    const { fetch, wrapper } = await render(
      () => Response.json({ title: 'Unavailable' }, { status: 503 }),
      () => Response.json({ data: [] }),
    );

    await wrapper.get('button').trigger('click');
    await flushPromises();

    expect(fetch).toHaveBeenCalledTimes(2);
    expect(wrapper.find('[role="alert"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('No containers yet.');
  });
});
