import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type PluginOption } from 'vite'
import vue from '@vitejs/plugin-vue'
import { dynamicBase } from 'vite-plugin-dynamic-base'

const vendorChunkGroups = [
  { name: 'vendor-vue', test: /node_modules[\\/](@vue|vue|vue-router|pinia)[\\/]/, priority: 40 },
  { name: 'vendor-vuetify-components', test: /node_modules[\\/]vuetify[\\/]lib[\\/]components[\\/]/, priority: 35 },
  { name: 'vendor-vuetify-core', test: /node_modules[\\/]vuetify[\\/]lib[\\/]/, priority: 30 },
  { name: 'vendor-mdi', test: /node_modules[\\/]@mdi[\\/]/, priority: 30 },
  { name: 'vendor-zrender', test: /node_modules[\\/]zrender[\\/]/, priority: 29 },
  { name: 'vendor-echarts-charts', test: /node_modules[\\/]echarts[\\/]lib[\\/]chart[\\/]/, priority: 28 },
  { name: 'vendor-echarts-components', test: /node_modules[\\/]echarts[\\/]lib[\\/]component[\\/]/, priority: 27 },
  { name: 'vendor-echarts-core', test: /node_modules[\\/]echarts[\\/]lib[\\/]/, priority: 26 },
  { name: 'vendor-echarts', test: /node_modules[\\/]echarts[\\/]/, priority: 25 },
  { name: 'vendor-vue-echarts', test: /node_modules[\\/]vue-echarts[\\/]/, priority: 25 },
  { name: 'vendor-signalr', test: /node_modules[\\/]@microsoft[\\/]signalr[\\/]/, priority: 20 },
  { name: 'vendor', test: /node_modules[\\/]/, priority: 10 },
]

function patchSignalRPureAnnotations(): PluginOption {
  return {
    name: 'headless-signalr-pure-annotation-patch',
    enforce: 'pre',
    transform(code, id) {
      if (!id.includes('@microsoft/signalr/dist/esm/Utils.js')) {
        return null
      }

      const patched = code.replace(
        /\/\*#__PURE__\*\/ function (getOsName|getRuntimeVersion)\(/g,
        '/* @__NO_SIDE_EFFECTS__ */ function $1(',
      )

      return patched === code ? null : patched
    },
  }
}

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
  const plugins: PluginOption[] = [
    patchSignalRPureAnnotations(),
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

  return {
    plugins,

    // Use dynamic base only in production, normal base in development
    base: mode === 'production' ? '/__dynamic_base__/' : '/',

    build: {
      outDir: 'dist',
      assetsDir: 'assets',
      rolldownOptions: {
        output: {
          codeSplitting: {
            groups: vendorChunkGroups,
          },
        },
      },
    },

    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    }
  }
})
