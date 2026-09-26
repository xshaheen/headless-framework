#!/usr/bin/env bash
# Regenerates every APNs oracle fixture: node-apn, pushy, then apns2.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --ignore-scripts keeps dependency lifecycle scripts off even where no user-level npmrc sets it.
(cd "$here/node" && npm ci --ignore-scripts --no-audit --no-fund && node generate.mjs)
(cd "$here/java" && mvn -q -B compile exec:java)
"$here/go/run.sh"
