/** A processor's result for one capture, separate from the source's asserted facts. */
export interface PostProcessingModel {
  readonly captureId: string;
  readonly sourceId: string;
  readonly linkId: string;
  readonly runId: string;
  readonly processor: string;
  readonly isCurrent: boolean;
  readonly processedAtUtc?: string | null;
  readonly runStatus: string;
  readonly requiresReview?: boolean;
  readonly output?: unknown;
}
