# Headless.Api.DataProtection

Extends ASP.NET Core Data Protection to persist encryption keys to blob storage providers.

## Problem Solved

In distributed/containerized environments, ASP.NET Core Data Protection keys must be shared across instances. This package enables key persistence to any `IBlobStorage` implementation (Azure, AWS S3, local filesystem, etc.).

## Key Features

- `PersistKeysToBlobStorage()` extension for `IDataProtectionBuilder`
- Works with any `IBlobStorage` implementation
- Ensures the `DataProtection` container before writes when an `IBlobContainerManager` is registered or supplied
- Supports factory-based storage resolution for DI scenarios, including keyed/named stores via a `serviceKey` overload
- Enforces container provisioning up front: when no manager is available and the storage requires a provisioned container (`IBlobStorage.RequiresContainerProvisioning`), configuration throws unless you acknowledge out-of-band provisioning with `provisioning: BlobContainerProvisioning.PreProvisioned`
- `ValidateKeyRingAtStartup()` — opt-in startup gate (runs before other hosted services) that exercises the key ring, verifies write access with a real sentinel write, and fails an empty key ring on read-only nodes — converting first-write/rotation failures into deploy-time failures
- `AddDataProtectionKeyRing()` — opt-in readiness health check, the continuous complement to the startup gate: re-validates the key-ring store on every health probe, catching a container deleted or write permission revoked after boot. The default probe is the definitive sentinel write (so `Healthy` uniformly means "the key ring can be persisted"); `KeyRingProbeStyle.ContainerExistence` is a cheap explicit opt-down that does not verify write access
- Key-ring reads are bounded to 1 MiB per XML blob, 1,000 blobs, and 16 MiB aggregate XML. DTD processing is prohibited. Exceeding any resource limit aborts the complete key-ring load; malformed XML within the limits is skipped as before.
- The container ensure runs inside the same retry pipeline as the key upload, and a terminal write failure surfaces as `InvalidOperationException` naming the `DataProtection` container, whether a manager was wired, and the remediation (original exception preserved as inner)

## Installation

```bash
dotnet add package Headless.Api.DataProtection
```

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage()
    // Opt-in: probe the key ring at startup so a missing container / bad credentials fails the deploy,
    // not the first key write or the ~90-day rotation months later.
    .ValidateKeyRingAtStartup();

// Opt-in continuous complement to the startup gate: a readiness health check that re-validates the
// key-ring store on every health probe — see "Key-ring health check" below.
builder.Services.AddHealthChecks().AddDataProtectionKeyRing();

// Or with explicit storage instance (no manager: throws at config time for provisioning-requiring backends
// unless you acknowledge out-of-band provisioning — see Configuration)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, provisioning: BlobContainerProvisioning.PreProvisioned);

// Or with explicit storage + container manager (ensures the DataProtection container before writes)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(storageInstance, containerManager);

// Or with factory
builder.Services.AddDataProtection().PersistKeysToBlobStorage(sp => sp.GetRequiredService<IBlobStorage>());

// Or against a named/keyed blob store (resolves the keyed IBlobStorage + IBlobContainerManager)
builder.Services.AddDataProtection().PersistKeysToBlobStorage(serviceKey: "keys");
```

## Configuration

No specific configuration. Depends on the underlying `IBlobStorage` configuration. Cloud/object-store providers should also register or pass the matching `IBlobContainerManager` so the `DataProtection` container is created before the first key write.

The blob data plane treats a missing container as an error (not auto-created), so a repository with no `IBlobContainerManager` can never fix a missing `DataProtection` container — the first key write on a fresh deployment would fail. This is now **enforced**, not just advised: whenever the effective manager is `null` and the storage reports `IBlobStorage.RequiresContainerProvisioning == true`, configuration throws `InvalidOperationException` (at call time for the storage-instance overload; at first options resolution for the DI/factory/keyed overloads) unless you pass `provisioning: BlobContainerProvisioning.PreProvisioned` to acknowledge that the container was provisioned out-of-band (portal, CLI, IaC).

Provisioning matrix:

| Scenario | What to do |
| --- | --- |
| Manager available (registered, keyed, or passed) | Nothing — the `DataProtection` container is ensured before writes; no acknowledgment is needed |
| No manager, provisioning-requiring backend (AWS, Azure, FileSystem, SSH) | Wire an `IBlobContainerManager` (preferred), or provision the container out-of-band and pass `provisioning: BlobContainerProvisioning.PreProvisioned` |
| Cloudflare R2 | `provisioning: BlobContainerProvisioning.PreProvisioned` is the only option — R2 deliberately ships no `IBlobContainerManager` (object-scoped tokens cannot create buckets); create the bucket in the Cloudflare dashboard/API first |
| Redis (`RequiresContainerProvisioning == false`) | Exempt — the backing hash materializes on first write; the storage-only overload works with no acknowledgment |

### Startup validation (`ValidateKeyRingAtStartup`)

Key writes are lazy: the first write happens on first boot and again at the ~90-day key rotation, so a misconfigured container, manager, or credential can stay hidden for months post-deploy and then take down data protection in production. `ValidateKeyRingAtStartup()` registers an opt-in startup gate (an `IHostedLifecycleService` whose probe runs in `StartingAsync`, **before** any registered `IHostedService.StartAsync` — including the framework's own data-protection hosted service and your application services) that surfaces this at boot:

- **`KeyManagementOptions.AutoGenerateKeys == true` (default)** — the probe protects and unprotects a small payload through the real provider. On a fresh deployment this generates a key and drives the full persistence path (`StoreElement` → container ensure → blob upload), so any container/permission problem fails the deploy.
- **`AutoGenerateKeys == false` (designated-key-writer topologies)** — the probe reads the key ring via `IKeyManager.GetAllKeys()` (exercising the repository read path) and never forces key generation. A **reachable-but-empty key ring fails validation**: the node would have no usable key for its first protected operation (has the designated key writer run? is this the right container?).
- **Write probe (both modes, `ProbeWritePath`, default `true`)** — write access is verified with a real write: a reserved sentinel blob (`startup-write-probe.xml`) is uploaded to and deleted from the `DataProtection` container through the same ensure + retry pipeline the key writes use. This is what catches lost write permission or a deleted container when a valid key already exists (the round-trip performs no write then), and the only write-path guarantee on read-only nodes. The sentinel name is reserved and always excluded from key-ring loading, so a crash between upload and delete is harmless. When a non-blob `IXmlRepository` is configured, the write probe is skipped with a debug log.

Failure handling is controlled by `DataProtectionStartupValidationOptions.Mode`:

| Mode | Behavior |
| --- | --- |
| `StartupValidationMode.Throw` (default) | `StartingAsync` throws `InvalidOperationException` with an actionable message naming the `DataProtection` container and the provisioning/manager remediation — host start fails |
| `StartupValidationMode.LogOnly` | The failure is logged at `Critical` level and startup continues |

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToBlobStorage(storageInstance, containerManager)
    .ValidateKeyRingAtStartup(options =>
    {
        options.Mode = StartupValidationMode.LogOnly; // default: Throw
        options.ProbeWritePath = true;                // default: true — sentinel upload+delete each boot
    });
```

Registration is idempotent — calling `ValidateKeyRingAtStartup` twice registers a single hosted service.

### Key-ring health check (`AddDataProtectionKeyRing`)

`ValidateKeyRingAtStartup` is a one-shot boot gate — it cannot see a container deleted or write permission revoked AFTER the host started. `AddDataProtectionKeyRing()` is the opt-in continuous complement: a readiness health check that re-validates the key-ring store on every health probe.

```csharp
builder.Services.AddHealthChecks()
    // Defaults: name "dataprotection-keyring", failure status Unhealthy, no tags,
    // probeStyle KeyRingProbeStyle.WriteProbe (the definitive probe).
    .AddDataProtectionKeyRing();
```

The probe is selected by `KeyRingProbeStyle`, not by the wiring, so `Healthy` has one meaning per registration (each probe reports a distinct description so operators can tell which ran):

| Style | Probe | Guarantee | Cost |
| --- | --- | --- | --- |
| `KeyRingProbeStyle.WriteProbe` (default) | Sentinel write probe — the reserved `startup-write-probe.xml` blob is uploaded and deleted through the same ensure + retry pipeline the key writes use, manager or not (crash-safe; the sentinel is always excluded from key-ring loading) | `Healthy` = the key ring can actually be persisted — exactly what the ~90-day rotation needs | One real write + delete per probe |
| `KeyRingProbeStyle.ContainerExistence` (explicit opt-down) | `ContainerExistsAsync("DataProtection")` via the wired `IBlobContainerManager` — a missing container fails the check ("key rotation will fail") | Existence only — does **not** verify write access: revoked write permission still reports `Healthy` while the next rotation write would fail | Cheap existence check |

With `ContainerExistence` and no manager wired (pre-provisioned mode), the check falls back to the write probe — the only probe possible — and says so in its description; that legitimate wiring does not report `Degraded`.

Statuses:

| Status | Meaning |
| --- | --- |
| `Healthy` | The probe that ran succeeded — with the default style that means the full persistence path is verified |
| `Degraded` | `KeyManagementOptions.XmlRepository` is not the blob-backed repository — registration misuse (nothing to check), not an outage |
| `Unhealthy` (default failure status; override via `failureStatus:`) | Container missing, the existence check threw, or the sentinel write failed — the probe exception is attached to the result |

**Probe interval note** — the default write probe performs a real write + delete per readiness ping, which can be chatty on tight probe intervals. Pair it with a probe interval you are comfortable with, or opt down to `probeStyle: KeyRingProbeStyle.ContainerExistence` and accept its weaker guarantee. The check deliberately does no caching or throttling of its own.

### Write resilience & failure context

The `DataProtection` container ensure (when a manager is wired) runs inside the same Polly retry pipeline as the key upload, so transient ensure failures are retried under the same predicate as the write. When a key write terminally fails (retries exhausted or a non-retried exception), the surfaced `InvalidOperationException` names the `DataProtection` container, states whether a manager was wired (ensure ran) or not (pre-provisioned mode), and points at the remediation — with the original backend exception preserved as the inner exception. The wrap adds context only; it never guesses a failure kind from the provider exception.

## Dependencies

- `Headless.Blobs.Abstractions`
- `Headless.Checks`
- `Azure.Extensions.AspNetCore.DataProtection.Blobs`
- `Microsoft.AspNetCore.DataProtection`
- `Microsoft.Extensions.Diagnostics.HealthChecks`
- `Microsoft.Extensions.Hosting.Abstractions`

## Side Effects

- Configures `KeyManagementOptions.XmlRepository` to use blob storage
- `ValidateKeyRingAtStartup()` registers an `IHostedLifecycleService` that probes the key ring in `StartingAsync`, before other hosted services start (with `AutoGenerateKeys`, the first key may be created at boot instead of at first use; with `ProbeWritePath`, a sentinel blob is written and deleted each boot)
- `AddDataProtectionKeyRing()` adds an `IHealthCheck` registration (default name `dataprotection-keyring`); with the default `KeyRingProbeStyle.WriteProbe`, each probe writes and deletes the sentinel blob
