import { fileURLToPath } from 'node:url'
import { dashboardModule } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

// The build lands where the .NET package embeds it, as Headless.Sequences.wwwroot.dist.modules.sequences.*.
export default defineConfig(
  dashboardModule({
    root: fileURLToPath(new URL('.', import.meta.url)),
    entry: 'src/module.ts',
    outDir: 'dist/modules/sequences',
  }),
)
