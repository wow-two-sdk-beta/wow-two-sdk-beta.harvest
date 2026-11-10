import type { ConsoleIconName } from './ConsoleIcon.vue';

/** One place in the sidebar; its label matches its route's title. */
export interface ConsoleDestination {
  readonly label: string;
  /** The route the destination opens, by its id. */
  readonly route: string;
  readonly icon: ConsoleIconName;
  /** Other routes that belong to this destination, such as one pipeline under Pipelines. */
  readonly within?: readonly string[];
}

/** One labelled group of destinations — SCFD's own flow, from source to subscriber. */
export interface ConsoleSection {
  readonly label: string;
  readonly destinations: readonly ConsoleDestination[];
}

/** The sidebar, in order: Overview, Collect, Content, Deliver, System. */
export const ConsoleSections: readonly ConsoleSection[] = [
  { label: 'Overview', destinations: [{ label: 'Home', route: 'home', icon: 'home' }] },
  {
    label: 'Collect',
    destinations: [
      { label: 'Sources', route: 'sources', icon: 'sources' },
      { label: 'Pipelines', route: 'pipelines', icon: 'pipelines', within: ['pipeline'] },
      { label: 'Runs', route: 'runs', icon: 'runs' },
    ],
  },
  { label: 'Content', destinations: [{ label: 'Posts', route: 'posts', icon: 'posts' }] },
  { label: 'Deliver', destinations: [{ label: 'Subscriptions', route: 'subscriptions', icon: 'subscriptions' }] },
  {
    label: 'System',
    destinations: [
      { label: 'Hosts', route: 'hosts', icon: 'hosts' },
      { label: 'Access', route: 'access', icon: 'access' },
    ],
  },
];

/** Finds the section and destination a route belongs to. */
export function destinationOf(route: string) {
  for (const section of ConsoleSections) {
    const destination = section.destinations.find((item) => item.route === route || item.within?.includes(route));
    if (destination) return { section, destination };
  }
  return undefined;
}
