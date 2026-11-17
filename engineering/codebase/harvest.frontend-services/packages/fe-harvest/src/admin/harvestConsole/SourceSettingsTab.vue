<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { SwitchInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import type { PipelineModel, SourceOverviewModel } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import SourceProfilePipelines from './SourceProfilePipelines.vue';
import { sourceProfileSchema } from './SourceProfileSchema';
import { useHarvestConsole, type SectionCheck } from './HarvestConsole';
import { sameSection, type SourceSection } from './SourceSections';
import { sourcePauseReason, sourceStateLabel } from './SourcePresentation';
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
 * its group and name. Catalog sections use the adapter's URLs; custom profiles use its URL schema and run bounds.
 */
defineOptions({ name: 'SourceSettingsTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const { pending, groups, capabilities } = session;
const canManageSource = computed(() => session.canOperate.value && session.canWrite('sources:manage'));
const canManagePipelines = computed(() => session.canOperate.value && session.canWrite('pipelines:manage'));
const capabilityError = computed(() => session.capabilityErrors.value[props.source.sourceId]);

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
const profileSchema = computed(() => sourceProfileSchema(metadata.value?.configuration.scopeSchema));
const canCapture = computed(() => session.canCapture(props.source.sourceId));
const canCheck = computed(() => metadata.value?.workKinds.includes('check') ?? false);

function scheduleOf(pipeline: PipelineModel | undefined, kind: 'capture' | 'check') {
  return pipeline?.schedules.find((row) => row.kind === kind);
}
function isHarvested(row: SectionRow) {
  return !!row.pipeline && row.pipeline.state !== 'paused' && !!scheduleOf(row.pipeline, 'capture')?.enabled;
}
function usesScheduledStages(row: SectionRow) {
  return row.pipeline?.schedules.some(
    (schedule) => schedule.kind === 'discover' || schedule.kind === 'harvest' || schedule.kind === 'process',
  );
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

/* The source master pause leaves each section's own schedule and pause unchanged. */
const sourceModel = computed(() => session.sourceOf(props.source.sourceId));
const sourceState = computed(() => props.source.sourceState ?? sourceModel.value?.state);
const sourceOn = computed(() => sourceState.value !== 'paused');
const readingReason = computed(() => sourcePauseReason(sourceState.value, props.source.sourceState !== undefined));
function setSourceReading(on: boolean) {
  if (!canManageSource.value || pending.value || readingReason.value) return;
  void session.setSourcePaused(props.source.sourceId, !on);
}
const aiAllowed = computed(() => sourceModel.value?.aiAllowed);
function setAiAllowed(allowed: boolean) {
  if (!canManageSource.value || pending.value || typeof aiAllowed.value !== 'boolean' || aiAllowed.value === allowed)
    return;
  void session.updateSource(props.source.sourceId, { aiAllowed: allowed });
}
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
  <div class="@container/settings grid min-w-0 gap-5">
    <section
      v-if="canManagePipelines"
      class="@container/sections min-w-0 self-start rounded-2xl border border-border bg-card"
      :aria-label="profileSchema?.fixedUrl ? 'Publisher' : profileSchema ? 'Profiles' : 'Sections'"
    >
      <div class="px-6 pt-5 pb-3">
        <h2 class="text-lg font-semibold">
          {{ profileSchema?.fixedUrl ? 'Publisher' : profileSchema ? 'Profiles' : 'Sections' }}
        </h2>
        <p class="mt-1 text-sm text-muted-foreground">
          {{
            profileSchema?.fixedUrl
              ? 'This source follows its declared publisher. Manage captures and limits in Pipelines.'
              : profileSchema
                ? 'Add a profile, then capture its posts when needed. Open its pipeline to inspect runs.'
                : 'Turn a section on to harvest it. A section also turns on when a subscriber follows it.'
          }}
        </p>
        <div v-if="capabilityError" class="mt-2 flex flex-wrap items-center gap-3 text-sm" role="status">
          <p>{{ capabilityError }}</p>
          <Button variant="outline" @click="session.retryCapabilities(props.source.sourceId)">Retry sections</Button>
        </div>
        <p v-else-if="!canCapture" class="mt-2 text-sm text-muted-foreground" role="status">
          {{
            metadata
              ? 'This source’s reader cannot harvest on this server yet.'
              : 'Reading the sections this source’s reader lists…'
          }}
        </p>
      </div>
      <div v-if="profileSchema?.fixedUrl" class="grid gap-3 px-6 pb-5">
        <p class="text-sm wrap-anywhere">{{ profileSchema.fixedUrl }}</p>
        <ConsoleLink
          :to="{ kind: 'source', id: props.source.sourceId, tab: 'pipelines' }"
          class="text-sm font-semibold text-primary underline-offset-2 hover:underline"
        >
          Manage publisher pipeline
        </ConsoleLink>
      </div>
      <div v-else-if="profileSchema" class="px-6 pb-5">
        <SourceProfilePipelines :source="source" :schema="profileSchema" />
      </div>
      <div v-else>
        <table class="block w-full text-sm @min-[43rem]/sections:table" role="table">
          <thead class="@max-[43rem]/sections:sr-only" role="rowgroup">
            <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
              <th class="px-6 py-2.5 font-semibold">Section</th>
              <th class="px-3 py-2.5 font-semibold">Harvest</th>
              <th class="px-3 py-2.5 font-semibold">Every</th>
              <th class="px-3 py-2.5 font-semibold">Check for changes</th>
              <th class="px-3 py-2.5 pe-6 font-semibold">Stop after</th>
            </tr>
          </thead>
          <tbody class="@max-[43rem]/sections:block" role="rowgroup">
            <tr
              v-for="row in rows"
              :key="row.key"
              class="border-b border-border last:border-b-0 @max-[43rem]/sections:grid @max-[43rem]/sections:grid-cols-[repeat(2,minmax(0,1fr))] @max-[43rem]/sections:gap-x-3 @max-[43rem]/sections:gap-y-3 @max-[43rem]/sections:p-4"
              role="row"
              v-bind="{ 'data-section': row.label }"
            >
              <th
                scope="row"
                role="rowheader"
                class="min-w-0 wrap-anywhere text-start font-semibold @max-[43rem]/sections:col-span-full @min-[43rem]/sections:px-6 @min-[43rem]/sections:py-3"
              >
                {{ row.label }}
              </th>
              <td role="cell" class="@min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3">
                <span aria-hidden="true" class="mb-1 block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Harvest
                </span>
                <SwitchInput
                  v-if="!usesScheduledStages(row)"
                  :model-value="isHarvested(row)"
                  :is-disabled="pending || (!row.pipeline && !canCapture)"
                  v-bind="{ 'aria-label': `Harvest ${row.label}` }"
                  @update:model-value="(on: boolean) => toggle(row, on)"
                />
              </td>
              <template v-if="row.pipeline">
                <td
                  role="cell"
                  class="min-w-0 @min-[43rem]/sections:w-44 @min-[43rem]/sections:px-3 @min-[43rem]/sections:py-2"
                >
                  <span
                    aria-hidden="true"
                    class="mb-1 block text-xs text-muted-foreground @min-[43rem]/sections:hidden"
                  >
                    Every
                  </span>
                  <ConsoleLink
                    v-if="usesScheduledStages(row)"
                    :to="{ kind: 'source', id: props.source.sourceId, tab: 'pipelines' }"
                    class="text-sm font-medium text-primary underline-offset-2 hover:underline"
                  >
                    Configure stages
                  </ConsoleLink>
                  <ChoicePicker
                    v-else
                    :model-value="everyOf(row)"
                    :choices="everyChoices(row)"
                    :label="`How often ${row.label} is harvested`"
                    :is-disabled="pending"
                    @update:model-value="
                      (value) => session.saveSectionSchedule(row.pipeline!.id, { everyHours: Number(value) })
                    "
                  />
                </td>
                <td
                  role="cell"
                  class="min-w-0 @min-[43rem]/sections:w-36 @min-[43rem]/sections:px-3 @min-[43rem]/sections:py-2"
                >
                  <span
                    aria-hidden="true"
                    class="mb-1 block text-xs text-muted-foreground @min-[43rem]/sections:hidden"
                  >
                    Check for changes
                  </span>
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
                <td
                  role="cell"
                  class="min-w-0 @min-[43rem]/sections:w-36 @min-[43rem]/sections:px-3 @min-[43rem]/sections:py-2 @min-[43rem]/sections:pe-6"
                >
                  <span
                    aria-hidden="true"
                    class="mb-1 block text-xs text-muted-foreground @min-[43rem]/sections:hidden"
                  >
                    Stop after
                  </span>
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
              <td
                v-else
                colspan="3"
                role="cell"
                class="text-sm text-muted-foreground @max-[43rem]/sections:col-span-full @min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3"
              >
                Not configured
              </td>
            </tr>
            <tr v-if="!rows.length && metadata && !capabilityError">
              <td colspan="5" class="px-6 py-4 text-sm text-muted-foreground">
                This source’s reader lists no sections.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <div v-if="canManageSource" class="grid min-w-0 gap-5 @min-[54rem]/settings:grid-cols-2">
      <section
        class="@container/fields min-w-0 rounded-2xl border border-border bg-card p-4 sm:p-6"
        aria-label="Reading"
      >
        <h2 class="text-lg font-semibold">Reading</h2>
        <dl
          class="mt-3 grid min-w-0 grid-cols-1 items-center gap-x-4 gap-y-2 text-sm @min-[25rem]/fields:grid-cols-[7.5rem_minmax(0,1fr)] [&>dd]:min-w-0 [&>dd]:mb-2 @min-[25rem]/fields:[&>dd]:mb-0"
        >
          <dt class="text-muted-foreground">Source</dt>
          <dd class="flex items-center gap-2 font-semibold">
            <SwitchInput
              :model-value="!readingReason && sourceOn"
              :is-disabled="pending || !!readingReason"
              v-bind="{ 'aria-label': `Read ${props.source.name}` }"
              @update:model-value="setSourceReading"
            />
            {{ readingReason ? sourceStateLabel({ ...props.source, sourceState }) : sourceOn ? 'Enabled' : 'Disabled' }}
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
          <dt class="text-muted-foreground">Model processing</dt>
          <dd class="flex items-center gap-2 font-semibold">
            <SwitchInput
              :model-value="aiAllowed === true"
              :is-disabled="pending || typeof aiAllowed !== 'boolean'"
              v-bind="{ 'aria-label': `Allow model processing for ${props.source.name}` }"
              @update:model-value="setAiAllowed"
            />
            {{ aiAllowed === true ? 'Enabled' : aiAllowed === false ? 'Disabled' : 'Unavailable' }}
          </dd>
        </dl>
        <p class="mt-3 text-xs text-muted-foreground">
          {{ readingReason || 'Disabling the source preserves each section’s settings.' }}
        </p>
        <p class="mt-2 text-xs text-muted-foreground">
          Model processing lets Process stages send saved text to their configured model.
        </p>
      </section>

      <section class="@container/fields min-w-0 rounded-2xl border border-border bg-card p-4 sm:p-6" aria-label="Group">
        <h2 class="text-lg font-semibold">Group</h2>
        <dl
          class="mt-3 grid min-w-0 grid-cols-1 items-center gap-x-4 gap-y-2 text-sm @min-[25rem]/fields:grid-cols-[7.5rem_minmax(0,1fr)] [&>dd]:min-w-0 [&>dd]:mb-2 @min-[25rem]/fields:[&>dd]:mb-0"
        >
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
            <form class="flex min-w-0 flex-wrap gap-2" aria-label="New group" @submit.prevent="createAndFile">
              <TextInput
                v-model="newGroup"
                placeholder="Name a new group"
                maxlength="120"
                class="min-w-0 flex-1"
                v-bind="{ 'aria-label': 'New group name' }"
              />
              <Button type="submit" variant="outline" :is-disabled="pending || !newGroup.trim()">Create</Button>
            </form>
          </dd>
          <dt class="text-muted-foreground">Name</dt>
          <dd>
            <form class="flex min-w-0 flex-wrap gap-2" aria-label="Name" @submit.prevent="saveName">
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
