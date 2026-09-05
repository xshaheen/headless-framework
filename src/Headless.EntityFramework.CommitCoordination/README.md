# Headless.EntityFramework.CommitCoordination

Adds commit coordination to the `Headless.EntityFramework` save pipeline.

```csharp
services
    .AddHeadlessDbContextServices()
    .AddCommitCoordination();
```

Install this adapter when work must be buffered against the active EF transaction and drained only after commit.
`Headless.EntityFramework.Messaging` installs it automatically for its transactional outbox bridge.

Coordinated Jobs write attempts prevent automatic retries of a pipeline-owned save because their separate context is not retained in the business change tracker. A later failure propagates unchanged; recover with a fresh context and aggregate graph after a known rollback, or reconcile an unknown commit first. Outbox-only saves retain their existing retry behavior.
