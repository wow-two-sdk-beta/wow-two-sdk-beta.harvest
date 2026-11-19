/** Mirrors `ClassificationVerdict`: a classification step's relevance verdict on a post. */
export type ClassificationVerdict = 'relevant' | 'irrelevant' | 'uncertain';

/** The entity types a `post-tags` answer names. */
export type ProcessedEntityType = 'event' | 'listing' | 'none' | 'unsure';

/** Mirrors `PostClassification`: the verdict a post carries, as subscribers receive it. */
export interface PostClassificationModel {
  readonly classifier: string;
  readonly verdict: ClassificationVerdict;
  readonly confidence?: number;
  readonly reason?: string;
  /** The post's kind in the subscriber's taxonomy, such as `runway`. */
  readonly kind?: string;
}

/** Mirrors `ProcessedAnswerModel`: who answered, with which prompt, at what cost, how fast and with how many tokens. */
export interface ProcessedAnswerModel {
  /** `anthropic`, `openai` or another provider; absent when no model answered. */
  readonly provider?: string;
  /** The model id, such as `claude-haiku-5-5`. */
  readonly model?: string;
  readonly promptVersion?: number;
  /** The attempt's cost in micro-USD, from the inference log; absent when the log holds no row. */
  readonly costMicroUsd?: number;
  /** The attempt's wall-clock time in milliseconds, from the inference log. */
  readonly latencyMs?: number;
  /** The attempt's input tokens, cached ones included, from the inference log. */
  readonly inputTokens?: number;
  /** The attempt's output tokens, reasoning included, from the inference log. */
  readonly outputTokens?: number;
  readonly answeredAt: string;
}

/** Mirrors `ProcessedDomainModel`: one taxonomy node of a tags answer. */
export interface ProcessedDomainModel {
  /** The node's key, such as `style-fashion/womens-fashion`. */
  readonly key: string;
  /** The node's English label. */
  readonly label: string;
  readonly primary: boolean;
}

/** Mirrors `ProcessedFlagModel`: a field the model or the checks were unsure of. */
export interface ProcessedFlagModel {
  readonly field: string;
  readonly reason: string;
  readonly proposed?: string;
}

/** Mirrors `ProcessedTagsModel`: a post's `post-tags` answer. */
export interface ProcessedTagsModel {
  /** `done`, `failed` or `unavailable`. */
  readonly status: string;
  readonly error?: string;
  /** Whether the answer is about the post's current title and text. */
  readonly current: boolean;
  readonly entityType?: ProcessedEntityType;
  /** The nodes the answer kept, the primary one first. */
  readonly domains: readonly ProcessedDomainModel[];
  readonly topics: readonly string[];
  readonly about?: string;
  /** `upcoming`, `ongoing`, `past`, `evergreen` or `unsure`. */
  readonly timeframe?: string;
  readonly titleEn?: string;
  readonly confidence?: number;
  readonly flags: readonly ProcessedFlagModel[];
  readonly answer: ProcessedAnswerModel;
}

/** Mirrors `ProcessedOccurrenceModel`: one run of an event as an extraction answer states it. */
export interface ProcessedOccurrenceModel {
  readonly starts?: string;
  readonly ends?: string;
  /** `year`, `month`, `day`, `hour` or `minute`. */
  readonly precision?: string;
  /** `stated` or `estimated`. */
  readonly certainty?: string;
}

/** Mirrors `ProcessedPriceModel`: a stated price; `label` is the period of a rent. */
export interface ProcessedPriceModel {
  readonly amount?: number;
  readonly currency?: string;
  readonly label?: string;
}

/** Mirrors `ProcessedEventSummary`: the parts of an `extract-event` answer a card shows. */
export interface ProcessedEventSummary {
  readonly name?: string;
  readonly originalName?: string;
  readonly editionLabel?: string;
  readonly occurrences: readonly ProcessedOccurrenceModel[];
  readonly venue?: string;
  readonly city?: string;
  readonly country?: string;
  readonly format?: string;
  /** `free`, `ticketed`, `application` or `unknown`. */
  readonly access?: string;
  readonly prices: readonly ProcessedPriceModel[];
  readonly status?: string;
}

/** Mirrors `ProcessedListingSummary`: the parts of an `extract-listing` answer a card shows. */
export interface ProcessedListingSummary {
  /** `sale`, `rent`, `service` or `unsure`. */
  readonly offerType?: string;
  readonly price?: ProcessedPriceModel;
  readonly rooms?: number;
  readonly areaM2?: number;
  readonly district?: string;
  readonly city?: string;
}

/** Mirrors `ProcessedExtractionModel`: a post's extraction answer about the same content as its tags. */
export interface ProcessedExtractionModel {
  /** `extract-event` or `extract-listing`. */
  readonly useCase: string;
  readonly status: string;
  readonly error?: string;
  /** `announcement`, `update`, `report`, `mention` or `unsure`. */
  readonly role?: string;
  readonly event?: ProcessedEventSummary;
  readonly listing?: ProcessedListingSummary;
  readonly flags: readonly ProcessedFlagModel[];
  readonly answer: ProcessedAnswerModel;
}

/** Mirrors `SourceProcessedPostModel`: one post with what the AI passes made of it. */
export interface SourceProcessedPostModel {
  readonly postId: string;
  readonly title: string;
  readonly url?: string;
  readonly revision: number;
  readonly firstSeenAt: string;
  /** The post's `PostKind`, such as `event` or `news`; omitted by an older host. */
  readonly kind?: string;
  /** The post's rolled-up existence state, such as `live`. */
  readonly state: string;
  /** When the post itself was published at its source; omitted when its reader cannot tell. */
  readonly postedAt?: string;
  /** The locale of the source's post link, or the post's extracted language. */
  readonly language?: string;
  /** The section whose run first captured the post from this source; omitted when no run did. */
  readonly pipelineId?: string;
  /** The post's first image, using the private stored copy when available; omitted when it has none. */
  readonly imageUrl?: string;
  readonly tags: ProcessedTagsModel;
  readonly classification?: PostClassificationModel;
  readonly extraction?: ProcessedExtractionModel;
}

/** Mirrors `SourceProcessedQuery`: the filters and page of `GET api/sources/{id}/processed`. */
export interface SourceProcessedQuery {
  readonly entityType?: ProcessedEntityType;
  readonly verdict?: ClassificationVerdict;
  /** Whether a done extraction answer must exist (`true`) or must not (`false`). */
  readonly extracted?: boolean;
  /** 1 to 90; the server's default is 30. */
  readonly limit?: number;
  readonly cursor?: string;
}

/** Mirrors `SourceProcessedPage`. */
export interface SourceProcessedPage {
  readonly items: readonly SourceProcessedPostModel[];
  readonly nextCursor?: string;
  /** How many processed posts match the filters, across every page. */
  readonly total: number;
}

/** Mirrors the processed route's page sizes. */
export const SourceProcessedLimits = { default: 30, max: 90 } as const;
