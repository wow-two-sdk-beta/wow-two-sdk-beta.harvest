import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import type { PostModel } from '../src/contracts';
import PostDetailsPanel from '../src/admin/harvestConsole/PostDetailsPanel.vue';

const post: PostModel = {
  id: 'post-1',
  containerId: 'container-1',
  state: 'live',
  revision: 2,
  base: {
    schema: 'wow2.post.v1',
    title: 'Source event',
    text: 'Complete source body.',
    dates: [{ role: 'start', value: '2026-10-27', precision: 'day' }],
    contacts: [{ kind: 'phone', value: '+998712051414', raw: '(+998) 71−205−1414' }],
    sourceTags: ['выставка', 'iteca'],
    extras: { source: { schema: 'fixture.v1', fields: { html: '<img src=x onerror=alert(1)>' } } },
  },
  links: [
    {
      id: 'link-1',
      sourceId: 'source-1',
      externalKey: 'source-event',
      url: 'https://source.example/event',
      state: 'live',
      role: 'primary',
      capturedAtUtc: '2026-10-27T17:00:00Z',
      lastCheckedAtUtc: '2026-10-27T17:00:00Z',
      lastSeenAtUtc: '2026-10-27T17:00:00Z',
      lastCheckOutcome: 'Present',
      lastCheckHttpStatus: 200,
      lastCheckRunKind: 'capture',
      nextCheckEligibleAtUtc: '2026-10-27T23:00:00Z',
      checkCount: 1,
    },
  ],
  fieldSources: { text: 'link-1' },
  firstSeenAtUtc: '2026-10-27T17:00:00Z',
  lastChangedAtUtc: '2026-10-27T17:00:00Z',
};

describe('PostDetailsPanel', () => {
  it('distinguishes capture evidence from scheduled checks and keeps native HTML inert', () => {
    const wrapper = mount(PostDetailsPanel, { props: { post, pipelines: [] } });

    expect(wrapper.text()).toContain('2026-10-27 · day precision');
    expect(wrapper.text()).toContain('+998712051414');
    expect(wrapper.text()).toContain('Source tags');
    expect(wrapper.text()).toContain('Present · HTTP 200');
    expect(wrapper.text()).toContain('The last observation came from capture');
    expect(wrapper.text()).toContain('Automatic check schedules: off');
    expect(wrapper.text()).toContain('execution depends on an enabled schedule and worker');
    expect(wrapper.text()).toContain('Initial source fields');
    expect(wrapper.find('img').exists()).toBe(false);
    expect(wrapper.text()).toContain('<img src=x onerror=alert(1)>');
    wrapper.unmount();
  });

  it('does not substitute a capture timestamp when an availability check is absent', () => {
    const unchecked = {
      ...post,
      links: [{ ...post.links[0]!, lastCheckedAtUtc: undefined, lastCheckRunKind: undefined }],
    };
    const wrapper = mount(PostDetailsPanel, { props: { post: unchecked, pipelines: [] } });
    const field = wrapper.findAll('dt').find((element) => element.text() === 'Last recorded availability check')!;

    expect(field.element.nextElementSibling?.textContent).toBe('Not recorded');
    expect(wrapper.text()).not.toContain('The last observation came from capture');
    wrapper.unmount();
  });
});
