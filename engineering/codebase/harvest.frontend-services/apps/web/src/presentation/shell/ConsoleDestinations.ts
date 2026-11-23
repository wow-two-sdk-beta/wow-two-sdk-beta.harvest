import type { ConsoleIconName } from './ConsoleIcon.vue';

/** One place in the sidebar; its label matches its route's title. */
export interface ConsoleDestination {
  readonly label: string;
  /** The route the destination opens, by its id. */
  readonly route: string;
  readonly icon: ConsoleIconName;
  /** Other routes that belong to this destination, such as one source under Sources. */
  readonly within?: readonly string[];
  /** Shown only to a session that operates SCFD. */
  readonly operatorOnly?: boolean;
}

/** One labelled group of destinations — SCFD's own flow, from source to subscriber. */
export interface ConsoleSection {
  readonly label: string;
  readonly destinations: readonly ConsoleDestination[];
}

/** The routes of one source's page: Overview, its tabs, and one of its posts. */
export const SourceRoutes = [
  'source',
  'source-posts',
  'source-processed',
  'source-processed-post',
  'source-post',
  'source-pipelines',
  'source-run',
  'source-activity',
  'source-settings',
] as const;

/** The routed views of a pipeline and its stages. */
export const PipelineRoutes = [
  'pipeline',
  'pipeline-logs',
  'pipeline-discoveries',
  'pipeline-run',
  'pipeline-stage',
  'pipeline-stage-logs',
  'pipeline-stage-discoveries',
  'pipeline-stage-run',
] as const;

/** The operator's sidebar, in order: Overview, Collect, Deliver, System. */
export const ConsoleSections: readonly ConsoleSection[] = [
  { label: 'Overview', destinations: [{ label: 'Home', route: 'home', icon: 'home' }] },
  {
    label: 'Collect',
    destinations: [
      { label: 'Sources', route: 'sources', icon: 'sources', within: SourceRoutes },
      { label: 'Available', route: 'available', icon: 'available', operatorOnly: true },
      { label: 'Pipelines', route: 'pipelines', icon: 'pipelines', within: PipelineRoutes },
      { label: 'Entities', route: 'entities', icon: 'entities', within: ['entity'] },
    ],
  },
  { label: 'Deliver', destinations: [{ label: 'Subscribers', route: 'subscribers', icon: 'subscribers' }] },
  {
    label: 'System',
    destinations: [
      { label: 'Access', route: 'access', icon: 'access' },
      { label: 'Media', route: 'media', icon: 'media', operatorOnly: true },
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

/** Whether a route is one source's page or one of its tabs. */
export function isSourceRoute(route: string) {
  return (SourceRoutes as readonly string[]).includes(route);
}

export function isPipelineRoute(route: string) {
  return (PipelineRoutes as readonly string[]).includes(route);
}
