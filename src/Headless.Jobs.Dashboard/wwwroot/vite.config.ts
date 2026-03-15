import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
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

export default defineConfig(async ({ command, mode }) => {
  const plugins = [
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
    plugins.splice(1, 0, vueDevTools({ launchEditor: undefined }))
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
    }
  }
})
