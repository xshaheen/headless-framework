import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type PluginOption } from 'vite'
import vue from '@vitejs/plugin-vue'
import { dynamicBase } from 'vite-plugin-dynamic-base'

function ensureNodeLocalStorage() {
  if (typeof globalThis.localStorage?.getItem === 'function') {
    return
  }

  const storage = new Map<string, string>()

  globalThis.localStorage = {
    getItem(key: string) {
      return storage.get(key) ?? null
    },
    setItem(key: string, value: string) {
      storage.set(key, value)
    },
    removeItem(key: string) {
      storage.delete(key)
    },
    clear() {
      storage.clear()
    },
    key(index: number) {
      return Array.from(storage.keys())[index] ?? null
    },
    get length() {
      return storage.size
    },
  }
}

// Demo backend URL — change if your demo runs on a different port
const DEMO_BACKEND = 'https://localhost:5111'

/**
 * Fetch a JWT from the demo backend's token endpoint.
 * Returns the token string, or null if the backend isn't reachable.
 */
async function fetchDevToken(): Promise<string | null> {
  // Allow self-signed certs for dev backend
  const originalTls = process.env.NODE_TLS_REJECT_UNAUTHORIZED
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
  try {
    const res = await fetch(`${DEMO_BACKEND}/security/createToken`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName: 'bob', password: 'bob' }),
    })
    if (!res.ok) return null
    // Results.Ok(token) wraps the string in JSON quotes — parse them out
    const body = await res.text()
    return body.replace(/^"|"$/g, '')
  } catch {
    console.warn('[vite] Could not fetch dev JWT — is the demo backend running?')
    return null
  } finally {
    if (originalTls === undefined) {
      delete process.env.NODE_TLS_REJECT_UNAUTHORIZED
    } else {
      process.env.NODE_TLS_REJECT_UNAUTHORIZED = originalTls
    }
  }
}

export default defineConfig(async ({ command, mode }) => {
  const plugins: PluginOption[] = [
    vue(),
    dynamicBase({
      // keep a single runtime variable; we'll set it in index.html
      publicPath: 'window.__dynamic_base__',
      transformIndexHtml: true,
    }),
  ]

  if (command === 'serve') {
    ensureNodeLocalStorage()
    const { default: vueDevTools } = await import('vite-plugin-vue-devtools')
    plugins.splice(1, 0, vueDevTools({ launchEditor: undefined }) as PluginOption)
  }

  // In dev, pre-fetch a JWT so the proxy can inject it into every request
  let devToken: string | null = null
  if (command === 'serve') {
    devToken = await fetchDevToken()
    if (devToken) {
      console.log('[vite] Dev JWT acquired — proxy will inject Authorization header')
    }
  }

  return {
    plugins,

    // Use dynamic base only in production, normal base in development
    base: mode === 'production' ? '/__dynamic_base__/' : '/',

    build: {
      outDir: 'dist',
      assetsDir: 'assets',
    },

    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },

    server: {
      proxy: {
        // Proxy API calls to the .NET demo backend
        '/messaging/api': {
          target: DEMO_BACKEND,
          changeOrigin: true,
          secure: false,
          configure: (proxy) => {
            proxy.on('proxyReq', (proxyReq) => {
              if (devToken) {
                proxyReq.setHeader('Authorization', `Bearer ${devToken}`)
              }
            })
          },
        },
      },
    },
  }
})
