import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'

// Tightly-scoped unit tests for pure, dependency-sensitive logic (cron parsing,
// URL/path resolution, UTC/date formatting). No component mounting — those tests
// are net-negative maintenance for internal dashboards. The Vite build + vue-tsc
// already gate compilation; this gates *behavior* across dependency bumps.
export default defineConfig({
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.spec.ts'],
    globals: false,
  },
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
})
