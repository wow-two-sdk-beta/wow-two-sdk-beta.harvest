/** Names the shared Files sizes that SCFD may generate or request for a post image. */
export type SourceImagePreset = 's1' | 's2' | 's3' | 's4' | 's5';

/** Holds the selected sizes for new post images and whether the Files connection is configured. */
export interface SourceImagePolicy {
  readonly postPresets: readonly SourceImagePreset[];
  readonly filesConfigured: boolean;
}
