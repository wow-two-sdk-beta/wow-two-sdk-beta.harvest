/** Represents the running backend and its local queue worker setting. */
export interface SystemStatus {
  readonly service: string;
  readonly version: string;
  readonly localWorkerEnabled: boolean | undefined;
}
