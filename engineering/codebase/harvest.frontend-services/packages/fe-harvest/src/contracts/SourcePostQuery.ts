import type { SourcePostModel } from './SourceOverviewModel';

export interface SourcePostQuery {
  readonly search?: string;
  readonly filters?: Readonly<Record<string, string>>;
  readonly cursor?: string;
  readonly limit?: number;
  readonly postId?: string;
}

export interface SourcePostFilterDefinition {
  readonly key: string;
  readonly label: string;
  readonly options: readonly { readonly value: string; readonly label: string }[];
}

export interface SourcePostFacet extends SourcePostFilterDefinition {
  readonly options: readonly { readonly value: string; readonly label: string; readonly count: number }[];
}

export interface SourcePostPage {
  readonly items: readonly SourcePostModel[];
  readonly nextCursor?: string | null;
  readonly total: number;
  readonly searchTotal: number;
  readonly facets: readonly SourcePostFacet[];
  readonly defaultPageSize: number;
  readonly maxPageSize: number;
}
