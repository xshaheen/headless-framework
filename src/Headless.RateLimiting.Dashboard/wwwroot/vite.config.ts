import { fileURLToPath } from 'node:url'
import { dashboardModule } from '@headless/dashboard-vite'
import { defineConfig } from 'vite'

// The build lands where the .NET package embeds it, as Headless.RateLimiting.wwwroot.dist.modules.ratelimiting.*. The
// folder has no hyphen because MSBuild rewrites a hyphen in an embedded folder name and the file provider does not.
export default defineConfig(
  dashboardModule({
    root: fileURLToPath(new URL('.', import.meta.url)),
    entry: 'src/module.ts',
    outDir: 'dist/modules/ratelimiting',
  }),
)
