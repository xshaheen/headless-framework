# Redis Script Loading Generalization Ideation

Date: 2026-06-02
Focus: Generalize `Headless.Redis` script loading so consumers load only the scripts they need.

## Grounding

Current branch shape:

- `Headless.Redis` owns `RedisScriptDefinition`, class-per-script definitions, a `RedisScriptCatalog`, and `HeadlessRedisScriptsLoader`.
- The loader now has two competing models: a preload-all `LoadScriptsAsync()` path and an on-demand `EvaluateAsync(IDatabase, RedisScriptDefinition, ...)` path.
- The loader still exposes feature-specific facade methods such as `TryAcquireSemaphoreAsync`, `SetIfHigherAsync`, and `ReplaceIfEqualAsync`.
- `Headless.Caching.Redis` still calls the older selector overload (`Func<HeadlessRedisScriptsLoader, LoadedLuaScript?>`), while `Headless.DistributedLocks.Redis` mostly calls feature-specific loader methods.
- `CLAUDE.md` says this is a modular NuGet framework, greenfield, and public API should stay deliberate.
- `src/Headless.Redis/README.md` and `docs/llms/utilities.md` still describe the deleted `RedisScripts` type and startup-style loading.

External grounding:

- Redis eval scripts are client-side application assets; Redis says script cache is volatile and apps must be ready to reload after restart, failover, or `SCRIPT FLUSH`. Source: https://redis.io/docs/latest/develop/programmability/eval-intro/
- Redis explicitly warns against dynamically generating many script variants; scripts should be generic and parameterized. Source: https://redis.io/docs/latest/develop/programmability/eval-intro/
- StackExchange.Redis already provides `LuaScript` and `LoadedLuaScript`; `LuaScript` can auto-cache on first evaluation, while `LoadedLuaScript` gives explicit load/eval control. Source: https://stackexchange.github.io/StackExchange.Redis/Scripting.html
- Redis Functions are a future alternative for named, persisted server-side logic, but they change operational ownership because functions live in Redis rather than remaining ephemeral client assets. Source: https://redis.io/docs/latest/develop/programmability/functions-intro/

## Topic Axes

1. API ownership: what `Headless.Redis` exposes as public NuGet contract.
2. Loading granularity: single script, group/bundle, or full preload.
3. Consumer integration: how `Caching.Redis` and `DistributedLocks.Redis` request scripts.
4. Failure/recovery semantics: NOSCRIPT, reconnects, cluster primaries, and stale loaded handles.
5. Authoring/testability: how new scripts stay discoverable, validated, and documented.

## Candidate Ideas Considered

### 1. Definition-first on-demand runtime

Keep `RedisScriptDefinition` as the unit of script ownership. Make the loader's primary API:

```csharp
await scripts.EvaluateAsync(db, ReplaceIfEqualScriptDefinition.Instance, parameters, ct);
```

Load exactly the requested definition on first use, cache it by definition type, recover from `NOSCRIPT` by reloading that definition, and keep explicit group preload as a secondary optimization.

Basis: direct code evidence: the current branch already added `RedisScriptDefinition`, class-per-script definitions, and an on-demand `EvaluateAsync` path. External: Redis script cache is volatile and reload-on-miss is a normal requirement.

Status: survivor, rank 1.

### 2. Script bundles for owned feature surfaces

Introduce a small `RedisScriptBundle` or `RedisScriptSet` type for named script groups:

```csharp
RedisScriptBundle Cache = RedisScriptBundle.Create([
    ReplaceIfEqualScriptDefinition.Instance,
    RemoveIfEqualScriptDefinition.Instance,
    IncrementWithExpireScriptDefinition.Instance,
]);
```

Feature packages can optionally preload their bundle during setup or startup, but runtime calls still evaluate individual definitions.

Basis: direct code evidence: the old catalog had `Cache` and `DistributedLocks` groups, which are useful but should not imply global preload. Reasoned: bundles preserve startup optimization without forcing apps to load unrelated scripts.

Status: survivor, rank 2.

### 3. Move feature-specific loader methods out of `Headless.Redis`

Remove methods like `TryAcquireSemaphoreAsync`, `SetIfHigherAsync`, and `ReleaseWriteLockAsync` from the generic loader over time. The owning package should own typed parameter objects and result decoding:

- `Headless.Caching.Redis` owns cache operation wrappers.
- `Headless.DistributedLocks.Redis` owns lock/semaphore/RW wrappers.
- `Headless.Redis` owns load/evaluate/recovery only.

Basis: direct code evidence: `Headless.Redis` currently knows cache and lock semantics, which couples the utility package to higher-level packages. `CLAUDE.md` says each package public surface is its NuGet contract, so generic packages should not expose provider-domain contracts accidentally.

Status: survivor, rank 3.

### 4. Endpoint-aware loaded-script cache

Track loaded state by definition plus writable endpoint/server identity rather than only by definition type. On load, mark each writable endpoint loaded. On reconnect or NOSCRIPT, invalidate either all endpoints or the affected definition. This matches Redis cluster/failover reality better than one global loaded handle.

Basis: external: Redis cache is per server and volatile after restart/failover. Direct code evidence: current code loops over writable endpoints but stores one `LoadedLuaScript` per definition type; that mostly works because hash/source is stable, but it hides endpoint state and makes partial failure semantics hard to reason about.

Status: survivor, rank 4.

### 5. `IRedisScriptExecutor` abstraction instead of exposing the loader

Expose a small interface:

```csharp
public interface IRedisScriptExecutor
{
    Task<RedisResult> EvaluateAsync(IDatabase db, RedisScriptDefinition definition, object? parameters, CancellationToken ct = default);
    ValueTask LoadAsync(IEnumerable<RedisScriptDefinition> definitions, CancellationToken ct = default);
}
```

Implementation remains `HeadlessRedisScriptsLoader`.

Basis: reasoned: feature packages depend on the behavior they need, not the concrete loader. This also makes tests less coupled to loader internals.

Status: survivor, rank 5, but only if there are 2+ consumers that need mocking or a DI seam. Otherwise keep the concrete type.

### 6. Source-generated script manifest and validation tests

Use a source generator or analyzer to discover `RedisScriptDefinition` subclasses and generate:

- built-in bundle manifests
- duplicate-type/name validation
- smoke tests that call `LuaScript.Prepare`
- docs tables

Basis: direct code evidence: manual catalog maintenance can drift as script count grows. Reasoned: this framework already uses source generation in other packages, but this would be heavy for the current script count.

Status: maybe later.

### 7. Replace eval scripts with Redis Functions

Ship function libraries and call `FCALL` instead of `EVALSHA`.

Basis: external: Redis Functions are named, persisted, replicated, and better for database-owned logic.

Status: rejected for now. It changes operational ownership, upgrade flow, Redis version assumptions, and failure mode. Headless scripts are currently client-owned provider implementation details; functions would require a deployment/migration story.

### 8. Keep a global built-in catalog and just make preload optional

Keep `RedisScriptCatalog.All` as the main public shape and rely on consumers not to call preload.

Basis: direct current shape.

Status: rejected. It preserves the wrong mental model: `Headless.Redis` as "all scripts for every feature" instead of a runtime plus typed definitions.

### 9. Let StackExchange.Redis auto-cache `LuaScript` and remove explicit loading

Use `LuaScript.EvaluateAsync` everywhere and rely on StackExchange.Redis script caching.

Basis: external: StackExchange.Redis supports auto-caching through `LuaScript`.

Status: rejected as the primary design. It is simple, but it gives less control over startup preloading, NOSCRIPT logging, reconnect behavior, and provider-wide metrics. It may still be useful as an implementation strategy behind `IRedisScriptExecutor` for simple scripts.

### 10. One loader per feature package

Register separate loader instances for cache, locks, semaphores, and reader-writer locks.

Basis: reasoned: it enforces package-local loading by construction.

Status: rejected. It duplicates connection-restored subscriptions, creates more state to reason about, and still does not solve generic script execution. Better: one executor, feature-owned definitions/bundles.

### 11. Declarative key metadata on definitions

Add metadata to each definition describing expected key parameter names and operation mode:

```csharp
public override IReadOnlySet<string> KeyParameters => ["key", "fenceKey"];
public override RedisScriptMode Mode => RedisScriptMode.Write;
```

Basis: external: Redis requires script key names to be explicit for correct cluster execution. StackExchange.Redis maps `RedisKey` parameters to `KEYS` automatically, but the code currently relies on convention.

Status: survivor, rank 6, as a validation/test enhancement after the main runtime shape settles.

### 12. Lazy bundle warmup on first feature registration

When `AddRedisCache` or `AddRedisDistributedLock` is called, register a hosted startup warmup for only that feature's bundle.

Basis: direct: setup methods already register `HeadlessRedisScriptsLoader`. Reasoned: warmup can reduce first-operation latency without global loading.

Status: maybe later. It adds hosted-service lifecycle behavior to packages whose current Redis operations work lazily. Use only if first-operation latency matters.

## Ranked Survivors

### 1. Definition-first on-demand runtime

This is the core design. The default path should load exactly the script being evaluated, cache by script definition type, recover from `NOSCRIPT` by reloading only that definition, and preserve Redis-family exceptions. It matches Redis cache volatility and keeps runtime cost proportional to what the app actually uses.

### 2. Feature-owned bundles as optional preload groups

Do not preload `All`. Define `RedisScriptBundle` groups such as `RedisCacheScripts.Bundle`, `RedisDistributedLockScripts.Bundle`, and maybe finer groups for mutex/semaphore/reader-writer locks. Preload is an opt-in optimization, not the semantic path.

### 3. Move domain wrappers out of `Headless.Redis`

`Headless.Redis` should not expose `TryAcquireSemaphoreAsync` or `SetIfHigherAsync` forever. Keep it as the script executor and script definitions package. Move typed parameter construction/result decoding into the owning provider packages so the package boundary matches the architecture.

### 4. Endpoint-aware loaded state

This is the main robustness improvement. The current type-keyed cache is probably adequate for the happy path, but Redis script cache is per server. The design should model loaded endpoints explicitly, especially for cluster and failover correctness.

### 5. Small executor interface if needed

Only introduce `IRedisScriptExecutor` if it materially improves DI/testing. Do not add an interface just for ceremony. If introduced, consumers should depend on the interface, and `HeadlessRedisScriptsLoader` becomes the default implementation.

### 6. Definition metadata and validation

Once definitions are classes, add metadata only where it enforces real invariants: expected Redis key parameters, read/write mode, maybe feature owner. Use tests/analyzers to prevent scripts from hiding key names in ARGV or generated strings.

## Recommended Direction

The cleanest design is:

```csharp
public abstract class RedisScriptDefinition
{
    public string Name { get; }
    protected abstract string Source { get; }
}

public sealed class HeadlessRedisScriptsLoader
{
    public Task<RedisResult> EvaluateAsync(
        IDatabase db,
        RedisScriptDefinition definition,
        object? parameters,
        CancellationToken cancellationToken = default);

    public ValueTask LoadAsync(
        IEnumerable<RedisScriptDefinition> definitions,
        CancellationToken cancellationToken = default);
}
```

Provider packages then use:

```csharp
await scripts.EvaluateAsync(
    db,
    ReplaceIfEqualScriptDefinition.Instance,
    parameters,
    cancellationToken);
```

And optional preload becomes:

```csharp
await scripts.LoadAsync(RedisCacheScriptBundle.Definitions, cancellationToken);
```

Avoid a global public `All`. If an internal `All` remains, it should exist only for tests or package-owned warmup, not as the main consumer model.

## Rejected Design Principles

- Do not put every Redis script behind one global preload path.
- Do not let `Headless.Redis` accumulate typed methods for every Redis-backed feature package.
- Do not introduce Redis Functions until there is a deliberate deployment story.
- Do not add a second loader per package when one executor plus bundles solves the same problem.
- Do not rely on dynamic Lua string generation.

## Open Questions For Brainstorming

1. Should `RedisScriptDefinition` classes be public, or should packages expose only bundles/wrappers and keep individual definitions internal?
2. Should `LoadScriptsAsync()` be renamed to `LoadAsync(IEnumerable<RedisScriptDefinition>)` and remove parameterless preload?
3. Should cache/lock convenience methods in `HeadlessRedisScriptsLoader` be removed now while the project is still greenfield?
4. Is endpoint-aware cache worth doing in the same change, or should the first pass keep definition-type caching and add endpoint state later?
5. Should `Headless.Caching.Redis` stop using selector delegates immediately and move to definition evaluation?
