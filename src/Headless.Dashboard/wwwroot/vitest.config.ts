import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    projects: [
      {
        test: {
          name: 'vite',
          include: ['packages/vite/test/**/*.test.ts'],
          environment: 'node',
        },
      },
      {
        test: {
          name: 'sdk',
          include: ['packages/sdk/test/**/*.test.{ts,tsx}'],
          environment: 'jsdom',
          setupFiles: ['packages/sdk/test/setup.ts'],
        },
      },
    ],
  },
})
