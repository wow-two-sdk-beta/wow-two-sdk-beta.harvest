/** The labels a post card can carry that a list filter may state. */
export type PostLabel = 'kind' | 'section' | 'language' | 'removed';

/**
 * Which card labels each list filter states by its value. A section filter names the section and, as the adapter lists
 * a section with its language, the language too. A new filter registers here once; the card never checks a filter by
 * name.
 */
export const FilterLabels: Readonly<Record<string, readonly PostLabel[]>> = {
  kind: ['kind'],
  section: ['section', 'language'],
  state: ['removed'],
};

/**
 * The labels every shown card would repeat, so the card can drop them: those of a filter with exactly one value
 * selected. A filter with no value or several values states nothing, since the label still tells the cards apart; and
 * a search, a range or a period is not here at all, because a card's own value is finer than the filter.
 */
export function labelsStatedBy(selected: Readonly<Record<string, readonly string[]>>): ReadonlySet<PostLabel> {
  const stated = new Set<PostLabel>();
  for (const [filter, values] of Object.entries(selected)) {
    if (values.length !== 1) continue;
    for (const label of FilterLabels[filter] ?? []) stated.add(label);
  }
  return stated;
}
