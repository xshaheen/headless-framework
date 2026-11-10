import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    name: 'messaging',
    include: ['test/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: ['test/setup.ts'],
  },
})
