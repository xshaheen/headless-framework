import { fileURLToPath } from 'node:url'
import { dashboardModule } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

export default defineConfig(
  dashboardModule({
    root: fileURLToPath(new URL('.', import.meta.url)),
    entry: 'src/module.ts',
    outDir: '../../dist/modules/diagnostics',
  }),
)
