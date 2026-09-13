# Headless.EntityFramework.CommitCoordination

This opt-in adapter connects `Headless.EntityFramework`'s internal save-pipeline transaction seam to `Headless.CommitCoordination.EntityFramework`. Install it and chain `.AddCommitCoordination()` from `AddHeadlessDbContextServices(...)` when buffered work must enlist in the transaction opened by the Headless save pipeline. The core `Headless.EntityFramework` package otherwise keeps a no-op coordinator and carries no commit-coordination package reference. `Headless.EntityFramework.Messaging` installs it automatically for its transactional outbox bridge.

```csharp
services
    .AddHeadlessDbContextServices()
    .AddCommitCoordination();
```

The adapter enlists through `DatabaseFacade.EnlistCommitCoordination` (the EF interceptor signals the outcome) and reads the scope's `CommitRetryGuard`. It also ships the scope-free `ExecuteCoordinatedTransactionAsync` overloads for any `IHeadlessDbContext` (`HeadlessCoordinatedTransactionExtensions`), which source the request scope from the context.

Coordinated Jobs write attempts prevent automatic retries of a pipeline-owned save because their separate context is not retained in the business change tracker. A later failure propagates unchanged; recover with a fresh context and aggregate graph after a known rollback, or reconcile an unknown commit first. Outbox-only saves retain their existing retry behavior.
