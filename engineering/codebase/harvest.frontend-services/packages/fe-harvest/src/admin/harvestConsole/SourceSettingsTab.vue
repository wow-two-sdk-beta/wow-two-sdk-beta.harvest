<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { SwitchInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import type { PipelineModel, SourceOverviewModel } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import { useHarvestConsole, type SectionCheck } from './HarvestConsole';
import { sameSection, type SourceSection } from './SourceSections';
import { checkChoiceOf, hoursOf, intervalWords, paceWords, plural } from './SourceWords';

/** The intervals a section can be harvested at, in hours. @internal */
const EveryHours = [1, 3, 6, 12, 24];
/** The reading paces an operator picks from, in seconds. @internal */
const PaceSeconds = [2, 5, 10, 30];
/** The page limits a section can stop after. @internal */
const PageLimits = Array.from({ length: 20 }, (_, index) => index + 1);
/** The group choice that files a source under none. @internal */
const NoGroup = 'none';

/**
 * A source's Settings, for operators only: every section the adapter lists, each with its harvest on or off, how often
 * it is harvested and checked for changes and where it stops; how the source is read — on or off, and its pace; and
 * its group and name. Turning a section on that nobody harvests creates its harvest from the section's own URL — nothing
 * here types a URL. Each change applies at once.
 */
defineOptions({ name: 'SourceSettingsTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const { pending, groups, capabilities } = session;

interface SectionRow {
  readonly key: string;
  readonly label: string;
  readonly section?: SourceSection;
  readonly pipeline?: PipelineModel;
}

const pipelines = computed(() => session.pipelines.value.filter((item) => item.sourceId === props.source.sourceId));
const rows = computed<SectionRow[]>(() => {
  const listed = session.sectionsOf(props.source.sourceId).map((section) => ({
    key: section.url,
    label: section.label,
    section,
    pipeline: pipelines.value.find((item) => !!item.scope.url && sameSection(item.scope.url, section.url)),
  }));
  const extra = pipelines.value
    .filter((pipeline) => !listed.some((row) => row.pipeline?.id === pipeline.id))
    .map((pipeline) => ({ key: pipeline.id, label: session.sectionLabel(pipeline.id), pipeline }));
  return [...listed, ...extra];
});
const metadata = computed(() => capabilities.value[props.source.sourceId]);
const canCapture = computed(() => session.canCapture(props.source.sourceId));
const canCheck = computed(() => metadata.value?.workKinds.includes('check') ?? false);

function scheduleOf(pipeline: PipelineModel | undefined, kind: 'capture' | 'check') {
  return pipeline?.schedules.find((row) => row.kind === kind);
}
function isHarvested(row: SectionRow) {
  return !!row.pipeline && row.pipeline.state !== 'paused' && !!scheduleOf(row.pipeline, 'capture')?.enabled;
}
function everyOf(row: SectionRow) {
  return String(hoursOf(scheduleOf(row.pipeline, 'capture')?.every) ?? 6);
}
function everyChoices(row: SectionRow) {
  const current = Number(everyOf(row));
  return [...new Set([...EveryHours, current])]
    .sort((a, b) => a - b)
    .map((hours) => ({ value: String(hours), label: intervalWords(`PT${hours}H`) }));
}
function checkOf(row: SectionRow) {
  const check = scheduleOf(row.pipeline, 'check');
  return checkChoiceOf(check?.enabled ? check.every : undefined);
}
function pagesOf(row: SectionRow) {
  const limit = Number(row.pipeline?.limits['maxPages']);
  return Number.isFinite(limit) && limit > 0 ? String(limit) : '';
}
function pageChoices(row: SectionRow) {
  const current = Number(pagesOf(row));
  return [...new Set([...PageLimits, ...(current > 0 ? [current] : [])])]
    .sort((a, b) => a - b)
    .map((pages) => ({ value: String(pages), label: plural(pages, 'page') }));
}
const checkChoices = [
  { value: 'off', label: 'off' },
  { value: 'daily', label: 'daily' },
  { value: 'weekly', label: 'weekly' },
];

function toggle(row: SectionRow, on: boolean) {
  if (row.section) void session.setSectionHarvest(props.source.sourceId, row.section, on);
  else if (row.pipeline) void session.setPipelineHarvest(row.pipeline.id, on);
}

/* Reading: the source is on while any of its sections is not paused. */
const sourceOn = computed(() => pipelines.value.some((pipeline) => pipeline.state !== 'paused'));
const pace = computed(() => String(Math.round(props.source.health.minDelayMs / 1000)));
const paceChoices = computed(() =>
  [...new Set([...PaceSeconds, Number(pace.value)])]
    .sort((a, b) => a - b)
    .map((seconds) => ({ value: String(seconds), label: paceWords(seconds * 1000) })),
);

/* Group and name. */
const name = ref(props.source.name);
const newGroup = ref('');
watch(
  () => props.source.name,
  (value) => {
    name.value = value;
  },
);
const groupChoices = computed(() => [
  { value: NoGroup, label: 'No group' },
  ...groups.value.map((group) => ({ value: group.id, label: group.name })),
]);
const groupValue = computed(() =>
  props.source.groupId && groups.value.some((group) => group.id === props.source.groupId)
    ? props.source.groupId
    : NoGroup,
);

function fileUnder(value: string) {
  void session.updateSource(
    props.source.sourceId,
    value === NoGroup ? { groupId: null, clearGroup: true } : { groupId: value },
  );
}
async function createAndFile() {
  const group = await session.createGroup(newGroup.value);
  if (!group) return;
  newGroup.value = '';
  await session.updateSource(props.source.sourceId, { groupId: group.id });
}
function saveName() {
  const value = name.value.trim();
  if (value && value !== props.source.name) void session.updateSource(props.source.sourceId, { name: value });
}
</script>

<template>
  <div class="grid min-w-0 gap-5 xl:grid-cols-[minmax(0,1fr)_24rem]">
    <section class="min-w-0 self-start overflow-hidden rounded-2xl border border-border bg-card" aria-label="Sections">
      <div class="px-6 pt-5 pb-3">
        <h2 class="text-lg font-semibold">Sections</h2>
        <p class="mt-1 text-sm text-muted-foreground">
          Turn a section on to harvest it. A section also turns on when a subscriber follows it.
        </p>
        <p v-if="!canCapture" class="mt-2 text-sm text-muted-foreground" role="status">
          {{
            metadata
              ? 'This source’s reader cannot harvest on this server yet.'
              : 'Reading the sections this source’s reader lists…'
          }}
        </p>
      </div>
      <div class="overflow-x-auto">
        <table class="w-full min-w-[44rem] text-[15px]">
          <thead>
            <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
              <th class="px-6 py-2.5 font-semibold">Section</th>
              <th class="px-3 py-2.5 font-semibold">Harvest</th>
              <th class="px-3 py-2.5 font-semibold">Every</th>
              <th class="px-3 py-2.5 font-semibold">Check for changes</th>
              <th class="px-3 py-2.5 pe-6 font-semibold">Stop after</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in rows"
              :key="row.key"
              class="border-b border-border last:border-b-0"
              v-bind="{ 'data-section': row.label }"
            >
              <td class="px-6 py-3 font-semibold">{{ row.label }}</td>
              <td class="px-3 py-3">
                <SwitchInput
                  :model-value="isHarvested(row)"
                  :is-disabled="pending || (!row.pipeline && !canCapture)"
                  v-bind="{ 'aria-label': `Harvest ${row.label}` }"
                  @update:model-value="(on: boolean) => toggle(row, on)"
                />
              </td>
              <template v-if="row.pipeline">
                <td class="px-3 py-2 w-44">
                  <ChoicePicker
                    :model-value="everyOf(row)"
                    :choices="everyChoices(row)"
                    :label="`How often ${row.label} is harvested`"
                    :is-disabled="pending"
                    @update:model-value="
                      (value) => session.saveSectionSchedule(row.pipeline!.id, { everyHours: Number(value) })
                    "
                  />
                </td>
                <td class="px-3 py-2 w-36">
                  <ChoicePicker
                    :model-value="checkOf(row)"
                    :choices="checkChoices"
                    :label="`How often ${row.label} is checked for changes`"
                    :is-disabled="pending || !canCheck"
                    @update:model-value="
                      (value) => session.saveSectionSchedule(row.pipeline!.id, { check: value as SectionCheck })
                    "
                  />
                </td>
                <td class="px-3 py-2 pe-6 w-36">
                  <ChoicePicker
                    :model-value="pagesOf(row)"
                    :choices="pageChoices(row)"
                    :label="`Pages ${row.label} stops after`"
                    placeholder="no limit"
                    :is-disabled="pending"
                    @update:model-value="
                      (value) => session.saveSectionSchedule(row.pipeline!.id, { maxPages: Number(value) })
                    "
                  />
                </td>
              </template>
              <td v-else colspan="3" class="px-3 py-3 pe-6 text-sm text-muted-foreground">not harvested</td>
            </tr>
            <tr v-if="!rows.length">
              <td colspan="5" class="px-6 py-4 text-sm text-muted-foreground">
                This source’s reader lists no sections.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <div class="flex min-w-0 flex-col gap-5">
      <section class="rounded-2xl border border-border bg-card p-6" aria-label="Reading">
        <h2 class="text-lg font-semibold">Reading</h2>
        <dl class="mt-3 grid grid-cols-[7.5rem_minmax(0,1fr)] items-center gap-x-4 gap-y-3 text-[15px]">
          <dt class="text-muted-foreground">Source</dt>
          <dd class="flex items-center gap-2 font-semibold">
            <SwitchInput
              :model-value="sourceOn"
              :is-disabled="pending || !pipelines.length"
              v-bind="{ 'aria-label': `Read ${props.source.name}` }"
              @update:model-value="(on: boolean) => session.setSourcePaused(props.source.sourceId, !on)"
            />
            {{ sourceOn ? 'on' : 'off' }}
          </dd>
          <dt class="text-muted-foreground">Reading pace</dt>
          <dd>
            <ChoicePicker
              :model-value="pace"
              :choices="paceChoices"
              label="Reading pace"
              :is-disabled="pending"
              @update:model-value="(value) => session.setReadingPace(props.source.sourceId, Number(value) * 1000)"
            />
          </dd>
        </dl>
      </section>

      <section class="rounded-2xl border border-border bg-card p-6" aria-label="Group">
        <h2 class="text-lg font-semibold">Group</h2>
        <dl class="mt-3 grid grid-cols-[7.5rem_minmax(0,1fr)] items-center gap-x-4 gap-y-3 text-[15px]">
          <dt class="text-muted-foreground">Group</dt>
          <dd>
            <ChoicePicker
              :model-value="groupValue"
              :choices="groupChoices"
              label="Group"
              :is-disabled="pending"
              @update:model-value="fileUnder"
            />
          </dd>
          <dt class="text-muted-foreground">New group</dt>
          <dd>
            <form class="flex gap-2" aria-label="New group" @submit.prevent="createAndFile">
              <TextInput v-model="newGroup" placeholder="Name a new group" maxlength="120" class="min-w-0 flex-1" />
              <Button type="submit" variant="outline" :is-disabled="pending || !newGroup.trim()">Create</Button>
            </form>
          </dd>
          <dt class="text-muted-foreground">Name</dt>
          <dd>
            <form class="flex gap-2" aria-label="Name" @submit.prevent="saveName">
              <TextInput v-model="name" maxlength="120" class="min-w-0 flex-1" v-bind="{ 'aria-label': 'Name' }" />
              <Button
                type="submit"
                variant="outline"
                :is-disabled="pending || !name.trim() || name.trim() === props.source.name"
              >
                Save
              </Button>
            </form>
          </dd>
          <dt class="text-muted-foreground">Reader</dt>
          <dd class="font-semibold">
            {{ metadata ? `${metadata.configuration.displayName}, from an adapter release` : '—' }}
          </dd>
        </dl>
      </section>
    </div>
  </div>
</template>
