import { readdirSync, readFileSync, writeFileSync } from 'node:fs'
import { extname, join } from 'node:path'
import { brotliCompressSync, constants, gzipSync } from 'node:zlib'

const compressible = new Set(['.js', '.mjs', '.css', '.svg', '.json', '.txt'])

// Below this a compressed body saves less than its own headers cost.
const minBytes = 1024

/**
 * Writes a Brotli (`.br`) and gzip (`.gz`) sibling beside each compressible file under `dirs`, which the dashboard
 * serves in place of the file when the request accepts it. A sibling that would not be smaller is not written.
 */
export function precompress(dirs: readonly string[]) {
  let written = 0

  for (const dir of dirs) {
    for (const entry of readdirSync(dir, { recursive: true, withFileTypes: true })) {
      if (!entry.isFile() || !compressible.has(extname(entry.name))) {
        continue
      }

      const path = join(entry.parentPath, entry.name)
      const content = readFileSync(path)

      if (content.length < minBytes) {
        continue
      }

      const brotli = brotliCompressSync(content, {
        params: {
          [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
          [constants.BROTLI_PARAM_SIZE_HINT]: content.length,
        },
      })
      const gzip = gzipSync(content, { level: 9 })

      if (brotli.length < content.length) {
        writeFileSync(`${path}.br`, brotli)
        written++
      }

      if (gzip.length < content.length) {
        writeFileSync(`${path}.gz`, gzip)
        written++
      }
    }
  }

  return written
}
