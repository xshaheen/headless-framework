# Messaging package-family composition probe

`NewAllNew` references every currently packable Messaging package at `MessagingPackageVersion` and compiles the public verb, lane, and delivery API. Its isolated NuGet configuration resolves `Headless.*` exclusively from `artifacts/packages-results`; other dependencies resolve from NuGet.org.

Run from the repository root:

```bash
make verify-messaging-package-compatibility
```

The target packs the current checkout, restores the complete Messaging family from those artifacts, and builds the consumer probe. Its lockfile is an ephemeral receipt for the exact package version, which changes as commits advance. Do not commit temporary package caches or restore logs.
