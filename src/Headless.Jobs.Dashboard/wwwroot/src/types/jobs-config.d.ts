declare global {
  interface Window {
    JobsConfig?: {
      basePath: string;
      backendDomain?: string;
      auth: {
        mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom';
        enabled: boolean;
        sessionTimeout: number;
      };
    };
  }
}

export {};