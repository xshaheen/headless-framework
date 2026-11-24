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
          sequence: { hooks: 'list' },
        },
      },
      {
        test: {
          name: 'shell',
          include: ['apps/shell/test/**/*.test.{ts,tsx}'],
          environment: 'jsdom',
          setupFiles: ['apps/shell/test/setup.ts'],
          sequence: { hooks: 'list' },
        },
      },
    ],
  },
})
