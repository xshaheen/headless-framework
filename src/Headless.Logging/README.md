# Headless.Logging

The Headless logging profile for API and worker hosts, built on Microsoft.Extensions.Logging.

## Why use this package

A production host needs the same logging setup every time: JSON console lines outside Development and readable lines inside it, UTC timestamps, the process, host, and build facts on every entry, trace correlation, levels that change without a restart, and redaction that no provider or enricher can bypass. `AddHeadlessLogging()` wires all of it from one call, with Microsoft's log enrichment and the Headless redaction, and `HeadlessBootstrapLogger` covers the crash that happens before the host is built.

## Install

```bash
dotnet add package Headless.Logging
```

## Documentation

- [Headless Framework](https://github.com/xshaheen/headless-framework#readme)
- [Logging guide](https://github.com/xshaheen/headless-framework/blob/main/docs/llms/logging.md#headlesslogging)
