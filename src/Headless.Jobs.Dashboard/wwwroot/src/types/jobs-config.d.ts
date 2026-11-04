export type JobsAuthMode = 'none' | 'basic' | 'apikey' | 'host';

declare global {
  interface Window {
    JobsConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: JobsAuthMode;
        enabled: boolean;
        signInAvailable: boolean;
      };
    };
  }
}
