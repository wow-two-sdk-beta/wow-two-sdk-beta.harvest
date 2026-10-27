<script setup lang="ts">
import type { PipelineModel, PostModel } from '../../contracts';

/** Shows mapped facts, native source evidence and independently recorded availability observations. */
defineOptions({ name: 'PostDetailsPanel' });
const props = defineProps<{ post: PostModel; pipelines: readonly PipelineModel[] }>();
const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const dateFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'medium',
  timeZone,
});

/** Formats an instant while leaving unknown timestamps explicit. @internal */
function date(value?: string) {
  if (!value) return 'Not recorded';
  const instant = new Date(value);
  return Number.isNaN(instant.getTime()) ? value : dateFormatter.format(instant);
}

/** Allows only browser-safe public link schemes. @internal */
function webUrl(value: string) {
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? url.href : undefined;
  } catch {
    return undefined;
  }
}

/** Reports configured check cadences independently of each link's next eligibility time. @internal */
function automaticChecks(sourceId: string) {
  return props.pipelines.some(
    (pipeline) =>
      pipeline.sourceId === sourceId &&
      pipeline.state === 'active' &&
      pipeline.schedules.some((schedule) => schedule.kind === 'check' && schedule.enabled),
  );
}

/** Serializes evidence as escaped text rather than rendering captured HTML. @internal */
function json(value: unknown) {
  return JSON.stringify(value, null, 2);
}
</script>

<template>
  <section aria-label="Post details" class="min-w-0 space-y-6">
    <header>
      <h3 class="text-lg font-semibold">{{ post.base.title ?? 'Post details' }}</h3>
      <p class="mt-2 text-xs text-muted-foreground">
        {{ post.base.kind ?? 'Post' }} · {{ post.state }} · Revision {{ post.revision }}
      </p>
      <p v-if="post.base.postedAt" class="mt-2 text-xs text-muted-foreground">
        Published {{ date(post.base.postedAt.value) }} · {{ timeZone }}
      </p>
    </header>

    <dl
      v-if="post.base.dates?.length || post.base.location || post.base.prices?.length || post.base.author?.name"
      class="grid gap-3 text-sm sm:grid-cols-2"
    >
      <div v-if="post.base.author?.name">
        <dt class="font-medium">Published by</dt>
        <dd class="mt-1">{{ post.base.author.name }}</dd>
      </div>
      <div v-if="post.base.prices?.length">
        <dt class="font-medium">Published price</dt>
        <dd v-for="(price, index) in post.base.prices" :key="index" class="mt-1">
          {{ price.raw ?? (price.free ? 'Free' : 'See mapped price range') }}
          {{ price.currency && !price.raw?.toUpperCase().includes(price.currency.toUpperCase()) ? price.currency : '' }}
        </dd>
      </div>
      <div v-for="(item, index) in post.base.dates" :key="index">
        <dt class="font-medium capitalize">{{ item.role }}</dt>
        <dd class="mt-1">{{ item.value }}{{ item.end ? ` – ${item.end}` : '' }} · {{ item.precision }} precision</dd>
      </div>
      <div v-if="post.base.location?.mentions.length">
        <dt class="font-medium">Location</dt>
        <dd v-for="(mention, index) in post.base.location.mentions" :key="index" class="mt-1">
          {{ mention.text }}
        </dd>
      </div>
      <div v-if="post.base.location?.geo">
        <dt class="font-medium">Coordinates</dt>
        <dd class="mt-1">{{ post.base.location.geo.latitude }}, {{ post.base.location.geo.longitude }}</dd>
      </div>
    </dl>

    <div v-if="post.base.media?.some((item) => item.kind === 'image')" class="grid grid-cols-2 gap-3">
      <a
        v-for="item in post.base.media.filter((media) => media.kind === 'image')"
        :key="item.url"
        :href="webUrl(item.url)"
        target="_blank"
        rel="noopener noreferrer"
      >
        <img
          :src="webUrl(item.url)"
          alt="Source post image"
          loading="lazy"
          class="max-h-64 w-full rounded-lg object-cover"
        />
      </a>
    </div>

    <div v-if="post.base.text">
      <h4 class="text-sm font-semibold">Full post text</h4>
      <p class="mt-3 whitespace-pre-wrap text-sm leading-relaxed">{{ post.base.text }}</p>
    </div>

    <div v-if="post.base.contacts?.length">
      <h4 class="text-sm font-semibold">Contacts published in the post</h4>
      <ul class="mt-3 space-y-2 text-sm">
        <li v-for="contact in post.base.contacts" :key="`${contact.kind}:${contact.value}`">
          <span class="font-medium">{{ contact.kind }}: {{ contact.value }}</span>
          <span v-if="contact.raw && contact.raw !== contact.value" class="ml-2 text-muted-foreground">
            {{ contact.raw }}
          </span>
        </li>
      </ul>
      <p class="mt-2 text-xs text-muted-foreground">
        Numbers are normalized from source text; ownership and reachability are unverified.
      </p>
    </div>

    <div v-if="post.base.sourceTags?.length">
      <h4 class="text-sm font-semibold">Source tags</h4>
      <p class="mt-2 text-sm">{{ post.base.sourceTags.join(' · ') }}</p>
    </div>
    <div v-if="post.base.tags?.length">
      <h4 class="text-sm font-semibold">Mapped categories</h4>
      <p class="mt-2 text-sm">{{ post.base.tags.map((tag) => tag.tag).join(' · ') }}</p>
    </div>

    <div v-if="post.base.links?.length">
      <h4 class="text-sm font-semibold">Links in the post</h4>
      <ul class="mt-3 space-y-2 text-sm">
        <li v-for="link in post.base.links" :key="link.url">
          <a
            :href="webUrl(link.url)"
            target="_blank"
            rel="noopener noreferrer"
            class="break-all text-primary underline"
          >
            {{ link.text || link.url }}
          </a>
          <span class="ml-2 text-muted-foreground">{{ link.kind }}</span>
        </li>
      </ul>
    </div>

    <div>
      <h4 class="text-sm font-semibold">Source availability</h4>
      <p class="mt-2 text-xs text-muted-foreground">
        Times shown in {{ timeZone }}.
        <span v-if="post.base.kind === 'event'">Availability is separate from event cancellation.</span>
      </p>
      <ul class="mt-3 space-y-4">
        <li v-for="link in post.links" :key="link.id" class="rounded-lg border border-border p-3 text-sm">
          <a
            :href="webUrl(link.url)"
            target="_blank"
            rel="noopener noreferrer"
            class="break-all text-primary underline"
          >
            {{ link.url }}
          </a>
          <dl class="mt-3 grid gap-2 text-xs sm:grid-cols-2">
            <div>
              <dt class="font-medium">Link state</dt>
              <dd>{{ link.state }}</dd>
            </div>
            <div>
              <dt class="font-medium">Content captured</dt>
              <dd>{{ date(link.capturedAtUtc) }}</dd>
            </div>
            <div>
              <dt class="font-medium">Last recorded availability check</dt>
              <dd>
                {{ date(link.lastCheckedAtUtc) }}{{ link.lastCheckRunKind ? ` · ${link.lastCheckRunKind} run` : '' }}
              </dd>
            </div>
            <div>
              <dt class="font-medium">Last confirmed present</dt>
              <dd>{{ date(link.lastSeenAtUtc) }}</dd>
            </div>
            <div>
              <dt class="font-medium">Latest evidence</dt>
              <dd>
                {{ link.lastCheckOutcome ?? 'Not recorded'
                }}{{ link.lastCheckHttpStatus ? ` · HTTP ${link.lastCheckHttpStatus}` : '' }}
              </dd>
            </div>
            <div>
              <dt class="font-medium">Recorded observations</dt>
              <dd>{{ link.checkCount ?? 0 }}</dd>
            </div>
          </dl>
          <p v-if="link.lastCheckRunKind === 'capture'" class="mt-3 text-xs text-muted-foreground">
            The last observation came from capture; a later dedicated check is not recorded here.
          </p>
          <p class="mt-3 text-xs text-muted-foreground">
            Automatic check schedules: {{ automaticChecks(link.sourceId) ? 'enabled' : 'off' }}.
            <span v-if="link.nextCheckEligibleAtUtc">
              Next eligible after {{ date(link.nextCheckEligibleAtUtc) }}; execution depends on an enabled schedule and
              worker.
            </span>
          </p>
          <details v-if="link.sourceFields" class="mt-3 text-sm">
            <summary class="cursor-pointer font-semibold">Initial source fields</summary>
            <pre class="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-muted p-3 text-xs">{{
              json(link.sourceFields)
            }}</pre>
          </details>
        </li>
      </ul>
    </div>

    <details v-if="post.base.extras?.source && !post.links.some((link) => link.sourceFields)" class="text-sm">
      <summary class="cursor-pointer font-semibold">Initial source fields</summary>
      <pre class="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-muted p-3 text-xs">{{
        json(post.base.extras.source)
      }}</pre>
    </details>
    <div v-if="post.base.flags?.length" class="text-xs text-muted-foreground">
      <h4 class="font-semibold">Extraction notes</h4>
      <ul class="mt-2 space-y-1">
        <li v-for="flag in post.base.flags" :key="flag">{{ flag }}</li>
      </ul>
    </div>
    <details class="text-sm">
      <summary class="cursor-pointer font-semibold">Mapped fields and extraction versions</summary>
      <pre class="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-muted p-3 text-xs">{{
        json(post.base)
      }}</pre>
    </details>
    <details class="text-sm">
      <summary class="cursor-pointer font-semibold">Field evidence</summary>
      <dl class="mt-3 space-y-2 text-xs">
        <div v-for="(linkId, field) in post.fieldSources" :key="field">
          <dt class="font-medium">{{ field }}</dt>
          <dd class="mt-1 break-all text-muted-foreground">
            {{ post.links.find((link) => link.id === linkId)?.externalKey ?? linkId }}
          </dd>
        </div>
      </dl>
    </details>
    <p v-if="!post.base.extraction?.ai" class="text-xs text-muted-foreground">AI analysis: no result recorded.</p>
  </section>
</template>
