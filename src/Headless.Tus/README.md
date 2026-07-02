# Headless.Tus

Base package for the Headless TUS (resumable upload) stack: the shared `tusdotnet` dependency plus
the protocol-level pieces every tus deployment needs regardless of storage provider — CORS defaults
for browser clients and the expired-uploads cleanup job.

## Problem Solved

Two gaps every tus deployment hits regardless of which store it uses:

- **Browsers hide tus response headers cross-origin.** Without the right
  `Access-Control-Expose-Headers`, clients like `tus-js-client` and Uppy cannot read
  `Location`/`Upload-Offset`, so every cross-origin upload fails on the first request. The exact
  header list is protocol lore that otherwise gets copy-pasted from blog posts.
- **Nothing removes expired uploads.** tusdotnet only reports `Upload-Expires`; unfinished uploads
  accumulate forever unless the application runs a cleanup job.

The package also pins the shared `tusdotnet` + `Headless.Hosting` references so every TUS provider
package aligns on one version.

## Key Features

- `TusCorsDefaults` — the tus 1.0.0 CORS surface as constants: `ExposedHeaders` (a superset of
  tusdotnet's `CorsHelper.GetExposedHeaders()`, adding the `Upload-Defer-Length` that HEAD responses
  carry), `AllowedHeaders` (request headers a client sends — no upstream equivalent), and
  `AllowedMethods` (includes the PATCH/DELETE that default CORS configs miss)
- `CorsPolicyBuilder.WithTusHeaders()` — applies all three in one call; origins and credentials stay
  the caller's decision
- `TusExpiredUploadsCleanupService` + `services.AddTusExpiredUploadsCleanup(...)` — background job
  that periodically calls `ITusExpirationStore.RemoveExpiredFilesAsync` (expired **incomplete**
  uploads only; completed uploads are never touched)

## Design Notes

**Cleanup targets incomplete uploads only.** The TUS Expiration extension covers unfinished
uploads; conforming Headless stores never report completed uploads as expired, so the job cannot
destroy finished data. The first pass runs immediately at startup (reclaiming uploads that
expired while the app was down), then every interval; the default 5-minute interval balances
reclaim latency against the store scan each pass performs — the expiration *window* itself is
configured on `DefaultTusConfiguration.Expiration`, not here.

**Store discovery via `ITusExpirationStore`.** The cleanup job binds to the tusdotnet capability
interface, not a concrete store. Headless store packages forward the registration (for example
`AddTusAzureStore`), so `AddTusExpiredUploadsCleanup()` composes with any of them; a manually
constructed store is registered with `services.AddSingleton<ITusExpirationStore>(store)`.

## Installation

```bash
dotnet add package Headless.Tus
```

Pulled in transitively by every `Headless.Tus.*` provider package.

## Quick Start

```csharp
using Headless.Tus;

// CORS for a browser tus client on another origin (SPA dev server, CDN frontend):
builder.Services.AddCors(options =>
    options.AddPolicy("tus", policy =>
        policy.WithOrigins("https://app.example.com").WithTusHeaders()));

// Remove expired incomplete uploads every 5 minutes (requires an ITusExpirationStore,
// e.g. registered by Headless.Tus.Azure's AddTusAzureStore):
builder.Services.AddTusExpiredUploadsCleanup();

var app = builder.Build();
app.UseCors("tus");
```

## Configuration

`TusExpiredUploadsCleanupOptions`:

| Option | Default | Notes |
|---|---|---|
| `Interval` | `5 minutes` | How often expired incomplete uploads are removed. Each pass scans the store's uploads — prefer coarser intervals for containers with many uploads. Must be positive. |

## Dependencies

- `tusdotnet`
- `Headless.Hosting`

## Side Effects

- `AddTusExpiredUploadsCleanup` registers a hosted service (`TusExpiredUploadsCleanupService`) and
  `TimeProvider.System` (TryAdd).
- No other DI registrations; `TusCorsDefaults` / `WithTusHeaders` are pure helpers.
