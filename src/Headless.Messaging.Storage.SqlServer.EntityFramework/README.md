# Headless.Messaging.Storage.SqlServer.EntityFramework

Connects `Headless.Messaging.Storage.SqlServer` to an EF Core `DbContext` and enables the transactional outbox.

This adapter promotes the inbox tier to `Transactional`: the current fenced inbox outcome, compatible enlisted application state, and captured durable Bus/Queue rows commit together. It does not make handler entry, `TransportDirect`, or external/non-enlisted effects exactly once. Terminal identity is retained for 30 days by default and can be overridden with consumer `InboxRetention(...)`; expiry or purge resets identity, while force reprocessing preserves linked provenance.

Install this adapter in addition to the raw SQL Server storage package when messaging should reuse a `DbContext` connection and enlist outbox writes in commit coordination.

Each transactional consume attempt shares one DI scope and configured `TContext` across the EF runner, consume middleware, and handler. The runner saves tracked changes after the handler returns and keeps the scope alive through commit or rollback. Explicit handler saves and captured durable Bus/Queue rows roll back with application state when inbox completion rejects the attempt fence.

EF execution-strategy retries are allowed only before handler entry. After entry, handler, save, commit, rollback, and disposal failures return to Messaging's fenced retry path; EF cannot transparently replay the handler within the reserved attempt. Ambiguous commit outcomes are still probed before deciding whether the attempt committed.

```csharp
services.AddHeadlessMessaging(setup => setup.UseEntityFramework<AppDbContext>());
```
