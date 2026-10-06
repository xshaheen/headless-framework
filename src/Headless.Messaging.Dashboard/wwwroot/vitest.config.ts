import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// Tightly-scoped unit tests for pure, dependency-sensitive logic (json-bigint
// precision, URL/path resolution, UTC/date formatting). The Vite build + vue-tsc
// already gate compilation; this gates *behavior* across dependency bumps. Component
// mounting is reserved for wiring that only exists once the component runs (what a
// page requests on mount, which sign-in form a mode renders); those specs mount with
// Vue's own createApp, so no component-testing library is needed.
export default defineConfig({
  plugins: [vue()],
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.spec.ts'],
    globals: false,
    // Vuetify's ESM build imports its own CSS, which Node cannot load when the package is externalized.
    server: { deps: { inline: ['vuetify'] } },
  },
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
})
