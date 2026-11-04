declare global {
  interface Window {
    MessagingConfig?: {
      basePath: string
      backendDomain?: string
      statsPollingInterval?: number
      auth: {
        mode: 'none' | 'basic' | 'apikey' | 'host'
        enabled: boolean
        /** Host mode only: whether the server can send a signed-out operator through the host's sign-in. */
        signInAvailable?: boolean
      }
    }
  }
}

export {}
