declare global {
  interface Window {
    MessagingConfig?: {
      basePath: string
      backendDomain?: string
      statsPollingInterval?: number
      auth: {
        mode: 'none' | 'basic' | 'apikey' | 'host' | 'custom'
        enabled: boolean
        sessionTimeout: number
      }
    }
  }
}

export {}
