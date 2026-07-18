# Headless.EntityFramework.CommitCoordination

Adds commit coordination to the `Headless.EntityFramework` save pipeline.

```csharp
services
    .AddHeadlessDbContextServices()
    .AddCommitCoordination();
```

Install this adapter when work must be buffered against the active EF transaction and drained only after commit.
`Headless.EntityFramework.Messaging` installs it automatically for its transactional outbox bridge.
