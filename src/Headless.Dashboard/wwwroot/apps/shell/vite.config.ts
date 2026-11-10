import { fileURLToPath } from 'node:url'
import { dashboardShell } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

export default defineConfig(
  dashboardShell({ root: fileURLToPath(new URL('.', import.meta.url)), outDir: '../../dist' }),
)
