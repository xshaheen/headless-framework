import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    name: 'leases',
    include: ['test/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: ['test/setup.ts'],
  },
})
