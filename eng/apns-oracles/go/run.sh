#!/usr/bin/env bash
# Runs the apns2 oracle generator inside a digest-pinned golang image, because Go is not a development prerequisite.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
oracles="$(cd "$here/.." && pwd)"
fixtures="$(cd "$oracles/../.." && pwd)/tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures"

# golang:1.27.0-alpine, multi-arch index digest.
image='golang:1.27.0-alpine@sha256:4c9fe60190a2a3350ddc51de80d0224b8a6698d12bdfc999fee45ea9d6c46dbc'

mkdir -p "$fixtures"

# -mod=readonly makes the build fail instead of editing go.mod or go.sum, so every module is verified against the
# committed go.sum. GOTOOLCHAIN=local stops the go command from downloading a different toolchain.
docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e GOCACHE=/tmp/gocache -e GOMODCACHE=/tmp/gomod \
  -e GOTOOLCHAIN=local -e GOFLAGS=-mod=readonly \
  -v "$oracles:/oracles:ro" \
  -v "$fixtures:/fixtures" \
  -w /oracles/go \
  "$image" \
  go run . -oracles /oracles -out /fixtures/apns2.json
