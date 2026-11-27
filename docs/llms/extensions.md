---
domain: Extensions
packages: Extensions, Primitives, Urls
---

# Extensions

> Base helpers for every Headless app: the result pattern, domain primitives and value objects, paging, and collection, threading, IO, reflection, and BCL extension methods.

## Orientation

Three packages make up this domain. None has DI registration or options: you construct the types and call the extension methods directly.

- **`Headless.Extensions`** is the base library that almost every `Headless.*` package references. It references `Headless.Primitives`, so every Primitives type is available through it.
- **`Headless.Primitives`** holds the value model alone: the result pattern, the built-in domain primitives, value objects, and paging. Reference it directly when a project needs only those types.
- **`Headless.Urls`** is a fluent, mutable `Url` builder and parser, derived from Flurl's `Url` API. `Headless.Extensions` does not reference it; add it when you need it.

To find a helper, search by capability, then read that entry in [Headless.Extensions](#headlessextensions). Many helpers are extension methods on BCL types and live in the `System.*` namespaces, so they appear without a `using`.

To define your own strongly-typed primitive, use the source generator in `Headless.Generator.Primitives` (see [utilities.md](utilities.md#headlessgeneratorprimitives)). The built-in primitives below come from the same generator.

## Agent Rules

- Return `ApiResult` or `ApiResult<T>` from service methods for expected failures (not found, conflict, validation) instead of throwing. Use `Result<TValue, TError>` when you need your own error type. Throw only for programmer errors and truly exceptional conditions.
- Create results through `Ok`, `Fail`, a typed factory such as `NotFound`, or the implicit conversion from `T` or `ApiResultError`. Never use a `default` `ApiResult` or `ApiResult<T>`: it is neither success nor failure, its try-accessors return `false`, and direct access or branch operations throw.
- Scale `Money` only by a `decimal`: `price * 1.15m`. `*` and `/` never take another `Money`. `+` and `-` take a `Money` and throw `InvalidOperationException` when the currency codes differ.
- Use `Money` when an amount travels with its currency code. `MoneyAmount` is a `decimal` primitive with no currency code; `GetRounded()` rounds it to 2 decimal places with banker's rounding (`MidpointRounding.ToEven`).
- Treat a `null` bound of `Range<T>` as infinity: a `null` `From` is unbounded below and a `null` `To` is unbounded above. Never use `null` as a concrete point value.
- Check the result of `KeyedAsyncLock`'s timeout overload and `TryLock` for `null`. `LockAsync(key, timeout, timeProvider, ct)` returns `null` on timeout instead of throwing, and `TryLock(key)` returns `null` while another caller holds the key. Only `LockAsync(key, ct)` never returns `null`.
- Await each `KeyedAsyncLock` `ValueTask` exactly once. Never await it twice, store it, or read `.Result` before it completes. Hold the result as a `KeyedAsyncLock.Releaser`, not as `IDisposable`, or each acquire boxes it.
- Never re-acquire a `KeyedAsyncLock` key on the same flow before you release it. The lock is not reentrant, so the second acquire deadlocks.
- Use `ForEachAsync` when order matters. `ParallelForEachAsync` does not preserve order and defaults to `Environment.ProcessorCount` degree of parallelism; pass `-1` for unlimited.
- Use `Task.WhenAllBoundedAsync(operations, maxConcurrency)` when every operation must run even if one fails. It keeps at most `maxConcurrency` in flight, returns results in input order, and surfaces the failure after all complete, like `Task.WhenAll`.
- Handle `RegexMatchTimeoutException` when you match untrusted input with `RegexPatterns.*`. Every pattern has a 100 ms match timeout.
- Register GUID generation with `AddHeadlessGuidGenerator()` from `Headless.Hosting` (see [utilities.md](utilities.md#headlesshosting)). Resolve the keyed `IGuidGenerator` for `SequentialGuidType.SqlServer` only for SQL Server clustered primary keys, and `SequentialGuidType.Version7` (the default) for byte-ordered stores. `Version7` fragments SQL Server clustered indexes.
- Use the `Headless.Validators` types (`EgyptianNationalIdValidator.IsValid`, `TryParse`, and the rest) for boolean checks. For FluentValidation rules such as `EgyptianNationalId()`, use `Headless.FluentValidation`, and for phone rules `Headless.PhoneNumbers` (see [utilities.md](utilities.md#headlessfluentvalidation)).
- Read claims with `HeadlessClaimsPrincipalExtensions` (`GetUserId()`, `GetRoles()`, `GetTenantId()`, `AddOrReplace(...)`). `GetRoles()` never returns `null`.
- Use the `Headless.IO` stream helpers (`GetAllBytesAsync()`, `GetAllText()`) instead of hand-rolled buffers. Use `SizeLimitedReadStream` when input past a limit must fail, and `ReadSlice` when the reader should see a clean end of stream at a fixed length.
- Point `AssemblyHelper.LoadAssemblies(...)` and `InvokeAllStaticMethods(...)` only at trusted, application-owned assemblies. They load into the default load context and run public static methods by name. Never point them at a user-writable upload, cache, or temp folder.
- Write the minutes suffix of `TimeUnit.Parse` in lowercase: `"5m"`. The `m` suffix is case-sensitive, so it is never read as months.
- Pass an `onError` handler to `action.Debounce(interval, timeProvider?, onError?)` when the action can throw. The action runs on a thread-pool timer callback, so without a handler its exception is swallowed.

## Core Concepts

### Result pattern vs. exceptions

`ApiResult` and `ApiResult<T>` carry either a value or an `ApiResultError`. `Result<TValue, TError>` and `Result<TError>` are the same idea with your own error type. `ApiResultError` is an abstract `record` with a closed set of subtypes: `NotFoundError`, `UnauthorizedError`, `ForbiddenError`, `ConflictError`, `ValidationError`, and `AggregateError`. Each exposes a machine-readable `Code` and a human `Message`.

The factories carry the same data as the exception path:

- Conflict takes a message, one `ErrorDescriptor`, or many. A message-only conflict uses `ApiResultErrorCodes.Default`.
- Unauthorized and forbidden keep their descriptors.
- Not found takes a `string`, `Guid`, `int`, or `long` key.
- Validation takes a field-keyed descriptor map. A string-only validation pair uses `ApiResultErrorCodes.ValidationFailed`.

### Domain primitives and value objects

Two kinds of strongly-typed values exist, and both have value equality:

- **Generated primitives** (`UserId`, `AccountId`, `MoneyAmount`, `Month`, `PhoneNumber`) implement `IPrimitive<T>`, wrap one value, validate it on creation, and come with JSON and `TypeConverter` support.
- **Hand-written value objects** (`Money`, `GeoCoordinate`, `FullGeoCoordinate`, `Range<T>`, `PreferredLocale`) validate in the constructor and throw on invalid input, so an instance that exists is valid. A `GeoCoordinate` never holds a latitude outside `[-90, 90]`.

Use them in domain signatures instead of a raw `Guid`, `decimal`, or `(double, double)`.

### GUID ordering strategy

`Guid.NewGuid()` fragments clustered indexes. `IGuidGenerator` takes the ordering from `SequentialGuidType`:

- `Version7` produces RFC 9562 UUIDv7 values that sort correctly in byte-ordered stores such as PostgreSQL. It is the default.
- `SqlServer` produces EF Core "comb" GUIDs arranged for SQL Server's `uniqueidentifier` sort order. SQL Server sorts the timestamp bytes of a `Version7` value last, so `Version7` fragments its clustered keys.

A store that persists GUIDs resolves the keyed strategy that matches it, not the unkeyed default.

### Deterministic time and time zones

Inject `TimeProvider` for every time your code reads, instead of `DateTime.UtcNow`, so tests drive it with `FakeTimeProvider`. For the `ICurrentTimeZone` abstraction, see [context.md](context.md).

- `FixedTimezoneTimeProvider` pins one `TimeZoneInfo` for local-time conversions.
- `ChangeableTimezoneTimeProvider.ChangeTimeZone(...)` changes the zone until you dispose its result. It supports one writer: nested changes restore out of order.
- `TimezoneConstants` resolves IANA zone IDs through `TimeZoneInfo.FindSystemTimeZoneById`. Linux and macOS read the system database. Windows resolves IANA IDs through ICU, so they fail in NLS or globalization-invariant mode.
- `timeProvider.Delay(...)` and `timeProvider.CreateCancellationTokenSource(...)` call the BCL's `TimeProvider` overloads of `Task.Delay` and `CancellationTokenSource`. If your project also references `Microsoft.Bcl.TimeProvider` and imports both `System` and `System.Threading.Tasks`, the call is ambiguous (CS0121). Remove that package reference.
- `NormalizeToUtc()` coerces a `DateTime` of untrusted `Kind` to UTC before it becomes a `DateTimeOffset`. External SDKs often return `Unspecified`.

---

## Headless.Extensions

The base utility library: extension methods, helpers, constants, and exceptions shared by every `Headless.*` package.

### Setup

```bash
dotnet add package Headless.Extensions
```

```csharp
using Headless.Primitives;

public async Task<ApiResult<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    var user = await _repo.FindAsync(id, ct);
    if (user is null)
        return ApiResult<User>.NotFound(entity: "User", key: id.ToString());

    return user; // implicit conversion from T to ApiResult<T>
}

// Consume without unwrapping.
var message = result.Match(
    success: u => $"Hello {u.Name}",
    failure: error => error.Message
);
```

**Verify:** no startup signal; the package registers nothing. Prove each behavior your code relies on with a unit test assertion, such as `keyedLock.TryLock(key)` returning `null` while another caller holds `key`.

#### Bounded vs. ordered iteration

```csharp
// Unordered, bounded concurrency (default: Environment.ProcessorCount).
await users.ParallelForEachAsync(degreeOfParallelism: 5, action: async u => await ProcessAsync(u));

// Ordered, sequential, with index + cancellation.
await users.ForEachAsync(async (u, index, token) => await ProcessAsync(u, index, token), ct);
```

#### Size-limited reads

```csharp
using Headless.IO;

await using var limited = new SizeLimitedReadStream(source, maximumBytes: 1024 * 1024, leaveOpen: true);

try
{
    await limited.CopyToAsync(destination, cancellationToken);
}
catch (StreamSizeLimitExceededException)
{
    // Reject the oversized input. The stream probes at most one byte beyond the limit.
}
```

#### Keyed async locking (stampede protection)

```csharp
using Headless.Threading;

var keyedLock = new KeyedAsyncLock();

// Unbounded: never returns null.
using (await keyedLock.LockAsync(key, ct))
{
    // critical section scoped to `key`
}

// Bounded: returns null on timeout — degrade instead of blocking forever.
using var releaser = await keyedLock.LockAsync(key, TimeSpan.FromSeconds(2), TimeProvider.System, ct);
if (releaser is null)
{
    // timed out — skip work, serve stale, or return a miss
}

// Non-blocking: null when another caller holds the key. Keep the struct when ownership moves elsewhere.
if (keyedLock.TryLock(key) is { } held)
{
    _ = RefreshInBackgroundAsync(key, held); // takes a KeyedAsyncLock.Releaser and disposes it when done
}
```

#### EF Core predicate composition

```csharp
using Headless.Linq;

var filter = PredicateBuilder.True<Product>()
    .And(p => p.Price > 0)
    .And(p => p.IsActive);

var products = await dbContext.Products.Where(filter).ToListAsync(ct);
```

#### Egyptian national ID

```csharp
using Headless.Validators;

if (EgyptianNationalIdValidator.IsValid("29901011234567")
    && EgyptianNationalIdValidator.TryParse("29901011234567", out var year, out var month, out var day, out var governorate))
{
    var birthDate = new DateOnly(year, month, day);
    // governorate is the Arabic governorate name
}
```

### API by namespace

- **`Headless.Collections`**: `ParallelForEachAsync` (bounded concurrency, unordered) and `ForEachAsync` (sequential and ordered, with index and cancellation overloads); `DetectChanges` (classifies items by key as added, removed, updated, or unchanged); `EquatableArray<T>` (an array with value equality); `VersionedRingBuffer<T>` (a thread-safe buffer of the newest N items with a change version to wait on); `ComparerFactory`; `TypeList` and `ITypeList`. The `HeadlessEnumerableExtensions` materializers `AsList`, `AsArray`, `AsICollection`, `AsIList`, `AsIReadOnlyCollection`, `AsIReadOnlyList`, `AsISet`, `AsHashSet`, and `AsDictionary` return the source itself when it already has the requested type, and otherwise copy it.
- **`Headless.Linq`**: `PredicateBuilder` composes EF Core `Expression<Func<T, bool>>` filters: `True` and `False` seeds, `And`, `Or`, `Not`, `AndNot`, `OrNot`, and folds over an `IEnumerable`.
- **`Headless.Threading`**: `KeyedAsyncLock` (per-key async mutual exclusion); `TransientRetry.RunAsync` (bounded retry of self-contained work); `ReconnectBackoff` (the wait of a reconnect loop); `HeadlessTaskExtensions` (`Forget`, `WithCancellation`, `GetResultOrDefault`, `WithAggregatedExceptions`, `DelayedAsync`, `WhenAllBoundedAsync`); `HeadlessAsyncExExtensions` (timeout and safe waits over Nito.AsyncEx primitives); `Async.RunSync` and `Async.Using`; `InterlockedExtensions.InterlockedRaiseTo` (a lock-free, raise-only maximum on a `ref long`).
- **`Headless.IO`**: `ReadOnlySequence<byte>.ToStream()`, `NonSeekableStream`, `ActionableStream` (runs a dispose action once), `ReadSlice`, `SizeLimitedReadStream` (throws `StreamSizeLimitExceededException` past its limit), `GetAllText`, `GetAllBytes`, `WriteText` and their `Async` forms, and `CalculateMd5Async`. `IMimeTypeProvider` and `MimeTypeProvider` map file names to content types. `FileNames` sanitizes untrusted names and builds trusted save names. `FileHelper` reads and writes with retry and saves batches without path traversal. `DirectoryHelper` compares paths the way the platform does and checks sub-directories.
- **`Headless`** (root namespace): `IGuidGenerator`, `SequentialGuidType`, `SequentialGuidGenerator`, and `SequentialGuid`; `DisposableFactory` and `IHasIsDisposed`; `FixedTimezoneTimeProvider`, `ChangeableTimezoneTimeProvider`, and `TimezoneConstants`; `CultureHelper`, `StringHelper`, `OsHelper`, `ProcessHelper`, and `ExecutableFinder`; the constants `RegexPatterns`, `LanguageCodes`, `EnvironmentNames`, `EnvironmentVariables`, `HeadlessDiagnostics` (named `ActivitySource` and `Meter` factories), `SqlErrorCodes`, `StorageIdentifier`, and `StorageProvider` (SQL identifier rules per provider); `RelationalFaults`, `RelationalVersionedWrite`, `FailureFingerprint`, `SqlLikePattern`, and `LogSanitizer`.
- **Exceptions** (root namespace): `EntityNotFoundException`, `ConflictException`, `UnauthorizedException`, `ForbiddenException`, `TooManyRequestsException`, and `ServiceUnavailableException`. Derive from `ServiceUnavailableException` when a dependency the request needs cannot answer: the Headless API exception handler maps it and every derived type to 503 with `Cache-Control: no-store`. Derive from any of the others to give a business failure its own catchable type. Each has a protected constructor that takes your own message with its payload (`ConflictException(message, errors, innerException)` requires at least one descriptor), and the handler answers a derived type exactly as it answers the base type ([api.md](api.md#map-your-own-exceptions-and-errors)).
- **`Headless.Reflection`**: `ReflectionHelper` (attribute lookup, cached open-generic subclass checks), `HeadlessTypeExtensions` (friendly names, nullable, enum, and `Task<T>` unwrapping, base-class enumeration), `TypeHelper`, `AssemblyInformation` (entry-assembly metadata and commit number), `AssemblyHelper`, and `ObjectPropertiesHelper` (cached by-name property writes: `TrySetProperty` with a value factory, `TrySetPropertyValue` with a value, and `TrySetPropertyToNull`).
- **`Headless.Validators`** (boolean checks, not FluentValidation rules): `EgyptianNationalIdValidator` (`IsValid`, and `TryParse` returning the birth date and governorate), `GeoCoordinateValidator`, `EmailValidator`, and `EnumNameValidator` (`IsDefinedName` and cached `GetNames`). `MobilePhoneNumberValidator` ships in `Headless.PhoneNumbers`.
- **`Headless.Http`**: `ContentTypes`, `HttpHeaderNames`, `BasicAuthenticationValue`, and `HeadlessHttpStatusCodeExtensions`.
- **`Headless.Security`**: `JwtClaimTypes`, `UserClaimTypes`, `AuthenticationConstants`, and `IPasswordGenerator`, `PasswordGenerator`, and `GeneratePasswordOptions`. The generator builds a password from your policy (length, required character sets, and `RequiredUniqueChars`) and uses `RandomNumberGenerator` for every pick and for the shuffle.
- **Smaller namespaces**: claims (`HeadlessClaimsPrincipalExtensions`), `Headless.Text` (`LookupNormalizer`, `IgnoreCaseStringComparer`, `FormattedStringValueExtractor`), `Headless.Network` (`IpAddressHelper`), `Headless.Xml` (`XmlHelper`: `IsValidXml`, `IsValidXmlAsync`, encode, and decode), and `Headless.UI` (`DebounceExtensions`).
- **BCL extensions** in `System.*` namespaces: `HeadlessStringExtensions`, `HeadlessDateTimeExtensions`, `HeadlessDateTimeOffsetExtensions`, `HeadlessTimeSpanExtensions`, `HeadlessNumberExtensions`, `HeadlessEnumExtensions`, `HeadlessEnumerableExtensions`, and more.
- **Processes**: `ProcessStartInfo.RunAsTaskAsync` and `ProcessHelper.RunAsTaskAsync` run to exit and return a `ProcessResult`. `ProcessStartInfo.RunAndStreamAsync` and `ProcessHelper.RunAndStreamAsync` return an `IAsyncEnumerable<ProcessStreamedOutput>` of output lines followed by the exit code. `Process.TryToKill` kills a process tree on a best-effort basis.

### Configuration

None. The package has no options and no DI registration.

### Design and runtime behavior

The package registers no services, starts no hosted service, and changes no process-level state.

- **`Money`.** `*` and `/` take a `decimal` factor and round the result to 2 decimal places with `MidpointRounding.ToEven`. `+` and `-` require matching currency codes. Comparison operators accept a `Money` or a `decimal`. `Money` is a total order: a list with mixed codes sorts by code, then by amount.
- **`Range<T>`.** Range-to-range containment, overlap, and `RemoveConflictRangeParts` compare the lower and upper bounds separately. Subtracting `[m, p]` from `[null, z]` can return both `[null, predecessor(m)]` and `[successor(p), z]`. The value-level containment overloads test a single point.
- **`KeyedAsyncLock`.** Acquiring a free key completes synchronously and allocates nothing; only a wait for a held key allocates. Disposing a `Releaser` twice, disposing a copy after the lock was released (even after another caller took the key), and disposing `default(Releaser)` all do nothing. The `LockAsync` overloads report argument, disposed, and cancellation failures through the returned `ValueTask`, never as a throw from the call. `TryLock` throws them directly. The timeout overload takes a `TimeProvider`, so tests drive it with `FakeTimeProvider`. `Dispose()` releases the lock's internal resources.
- **`TransientRetry.RunAsync`.** It makes at most `TransientRetry.MaxAttempts` (3) attempts and retries only a failure your `isTransient` predicate accepts. Before retry `n` it waits `n × U(10 ms, 50 ms)` on the `TimeProvider` you pass, so colliding callers do not retry in step and tests drive it with `FakeTimeProvider`. It checks cancellation before each attempt and during each delay. A rejected failure, or the last attempt's failure, propagates as thrown, never wrapped. Each attempt must start from scratch with its own connection and transaction. Never use it inside a caller's transaction, because only the transaction's owner can retry that work. An overload takes `maxAttempts` and a `retryDelay(failedAttempt)` function for contention on a different time scale. Both overloads accept a `Func<CancellationToken, ValueTask<T>>` or a `Func<CancellationToken, ValueTask>`, with identical attempts, delays, predicate, and cancellation.
- **`ReconnectBackoff`.** `WaitAsync(token)` waits the current `Delay`, then doubles the nominal delay from `FirstDelay` (1 second) up to `Ceiling` (30 seconds). Call `Reset()` once the connection is up. Each delay is jittered within `[0.75×, 1×]` of the nominal value, never below the 1-second floor or above 30 seconds, so a fleet that loses its server does not reconnect in step. `WaitAsync` returns `false` when the token is cancelled during the wait, instead of throwing. The waits run on `TimeProvider.System` unless you pass a clock; pass one only in a test of the loop itself. An instance is not thread-safe, so give each reconnect loop its own. `ReconnectBackoff.Jitter(nominal, floor, random)` exposes the band for a caller that keeps its own nominal delay and floor.
- **`VersionedRingBuffer<T>`.** It keeps the newest `Capacity` items and drops the oldest on `Add`. Every add, and every `Remove` or `Clear` that removes an item, increments `Version`. `Add(sequence => item)` passes the new version to your factory as the item's sequence, so sequences increase strictly in insertion order: store it on the item and filter on it to read only what changed since a version you saw. The factory and every predicate run under the buffer's lock, so read a clock inside the factory to keep timestamps in sequence order, keep them short, and never call back into the buffer from them. `ToListNewestFirst(match, limit)` returns a snapshot. `WaitForChangeAsync(knownVersion, token)` returns at once when the version already differs, and otherwise completes on the next change, resuming the waiter off the writer's thread. A factory that throws leaves the buffer and its version unchanged.
- **`ParallelForEachAsync` and `ForEachAsync`.** `ParallelForEachAsync` runs through `Parallel.ForEachAsync` with no order guarantee. `ForEachAsync` runs one item at a time, in order, and checks cancellation before each item.
- **`SqlLikePattern`.** `Escape` returns user text that matches itself literally, `StartsWith` appends `%` to the escaped text, and `Contains` wraps it in `%`. Each prefixes `\`, `%`, `_`, and `[` with `\`, so the query must name `\` as the escape character: `LIKE @p ESCAPE '\'`, or `EF.Functions.Like(column, pattern, SqlLikePattern.EscapeCharacter)`. Without the escape clause, SQL Server and SQLite treat `\` as an ordinary character and the pattern stops matching. One pattern serves every provider. The column's collation (or `ILIKE`) decides case sensitivity, not the pattern. A `null` value throws `ArgumentNullException`, so check the filter before you build its pattern.
- **`LogSanitizer.Sanitize(value, maxLength)`.** Pass every untrusted string through it before it becomes a log property or part of an exception message: a request header, a correlation ID, or a value read from a message broker. It strips every control character (line breaks and tab included), U+2028 and U+2029, the bidi overrides and isolates U+202A to U+202E and U+2066 to U+2069, and every surrogate code unit U+D800 to U+DFFF, so characters outside the Basic Multilingual Plane, such as emoji, disappear too. Of an ANSI escape sequence it strips only the ESC character, so `\u001b[31m` leaves the inert text `[31m`. A value longer than `maxLength` before stripping is cut and ends in `...`, which counts toward `maxLength`, even when stripping alone would have made it fit. A `maxLength` of 3 or less turns such a value into `...` alone. To cap a field at exactly N characters with no suffix, call `Sanitize(value)` and cut the result yourself. A value that needs no change is returned as the same instance, `null` returns `null`, and a negative `maxLength` throws `ArgumentOutOfRangeException`.
- **`RelationalFaults`.** It reads a caught database exception without a driver reference. `FindDatabaseException(exception)` returns the outermost `DbException` in the inner-exception chain. `GetSqlServerErrorNumbers(dbException)` returns every error number of a SqlClient `SqlException` (`Microsoft.Data.SqlClient` or `System.Data.SqlClient`) in reported order, or an empty list for another driver. `IsUniqueViolation(exception)` takes the exception as thrown (an EF `DbUpdateException` or the `DbException`) and recognizes SQLSTATE `23505` (PostgreSQL, or any driver that reports SQLSTATE), SQL Server `2627` or `2601` in any error, and SQLite extended code `2067` or `1555`. `IsUniqueViolation(exception, out var constraintName)` also returns the violated constraint or unique index name: PostgreSQL's `ConstraintName` field (Npgsql), or SQL Server's English message for `2627` and `2601`. The name is `null` when SQL Server localizes the message for the session's language, and always on SQLite, while the result stays `true`. Match SQL Server errors with `GetSqlServerErrorNumbers`, never with `SqlException.Number`, which holds only the first error, and SQL Server does not always report the duplicate key or deadlock first. Every member throws `ArgumentNullException` for a `null` argument.
- **`RelationalFaults.IsConcurrencyConflict`.** It recognizes unique violations, PostgreSQL `40001` and `40P01`, SQL Server `1205`, `3960`, and `3961` in any error, and SQLite busy or locked results. It accepts EF wrappers. After the failed transaction is disposed, these conflicts permit a fresh read and a conditional retry, even when commit reported the error. Connection failures, timeouts, and an unclassified `DbException` return `false`. A `false` result does not establish the commit outcome. An uncertain commit needs a storage check, not blind replay.
- **`RelationalVersionedWrite.ExecuteAsync(attempt, readVersion, expectedVersion, conflictMessage, cancellationToken)`.** It runs a write that may commit only while a group of rows still has the version the caller read. Use it in a custom storage provider's compare-and-write instead of your own retry loop. Each `attempt` opens its own transaction, checks the version inside it (throwing `DBConcurrencyException` on a mismatch), writes, computes the version the write leaves, calls `attempt.BeginCommit(resultVersion)`, commits, and returns that version. `readVersion` reads the group outside any transaction. On a concurrency conflict (`RelationalFaults.IsConcurrencyConflict`), even one reported by the commit, it reads again: a changed version throws `DBConcurrencyException`, and an unchanged one runs a fresh attempt, up to `MaxAttempts` (3), then throws `DBConcurrencyException` too. Any other failure after `BeginCommit` has an unknown outcome and is never replayed: it returns the current version when that equals the one the attempt meant to leave, and otherwise rethrows the original failure. Every other failure, a failed re-read, and cancellation rethrow the original failure. The version must change on every insert, update, or delete in the group, and new rows need fresh identities, or a lost reply can be confirmed wrongly. Under an EF Core execution strategy, run the strategy inside `attempt` and return its failure as a value, so the strategy never replays an uncertain commit.
- **`FailureFingerprint`.** It groups failures by cause for a dashboard. `Compute(exception)` returns `v1:` and 16 lowercase hex characters. `ForCause(cause)` returns `v1:` and a fixed cause name (`v1:lease-lapsed`) for a failure with no exception. `VersionPrefix` is `v1:`. Jobs stores the value on its run rows (see [jobs.md](jobs/operations.md#failure-fingerprint)).
  - `Compute` unwraps only `AggregateException` (its first inner exception) and `TargetInvocationException`. An exception your code wraps around another is fingerprinted as the wrapper.
  - It hashes the innermost exception type's full name and the top three stack frames whose declaring type is outside `System.*` and `Microsoft.*`, as `Namespace.Type.Method` without line numbers. A lambda, local function, or async state-machine frame counts as its declaring method. The message never takes part, because it carries IDs that split one cause into many groups.
  - Call `Compute` where you record the failure, while the exception has its stack trace. An exception that was never thrown hashes its type alone.
  - `ForCause` accepts only lowercase ASCII letters, digits, and hyphens, and refuses a 16-character all-hex name, so a fixed cause never equals a computed value. It throws `ArgumentException` otherwise.
  - A changed algorithm gets a new version prefix, so stored `v1:` values keep their groups.
- **`RegexPatterns`.** Every pattern is source-generated with `RegexOptions.ExplicitCapture` and a 100 ms `MatchTimeout`. Every whole-value pattern (email, IP, URL, slug, username, numbers, national ID, dates, and the `StorageIdentifier` patterns) ends in `\z`, so `"a@b.com\n"` does not validate. `EmailValidator` uses the HTML5 living-standard email pattern, which allows a domain without a dot. Pass `requireDotInDomainName` to require one.
- **String comparisons.** Claim type matching is `OrdinalIgnoreCase`. `GetRoles()` returns an ordinal `ImmutableHashSet<string>`, empty when there are no roles. `ExtraProperties` keys use `StringComparer.Ordinal`. `DirectoryHelper` compares paths case-insensitively on Windows and macOS and case-sensitively on Linux. ID and claim parsing uses `CultureInfo.InvariantCulture`.
- **`ObjectPropertiesHelper.TrySetPropertyValue`.** It evaluates its argument whether or not the target property exists. Pass a clock read, a new GUID, or any other value with a visible side effect through `TrySetProperty(obj, name, () => value)`, whose factory runs only when the property is set.
- **`DisposableFactory.Create<TState>`.** Pass a `static` lambda and put what it needs in `state` (a tuple works) to avoid a closure per call. The state is copied at creation, so later changes to a caller's struct local are invisible to the callback. The callback runs at most once across repeated `Dispose` and `DisposeAsync` calls.
- **IO.** `GetAllBytesAsync` and `ReadAllBytesAsync` use the framework's async `File.*` APIs and pre-size the buffer when the stream length is known. `FileHelper` retries a transient `IOException` up to three times with 100, 200, and 400 ms delays, rejects rooted paths and traversal sequences before writing, and bounds batch-save concurrency to `ProcessorCount`. `ActionableStream` runs its dispose action once across `Dispose`, `Close`, and `DisposeAsync`, and still disposes the inner stream when the action throws.
- **`RunAndStreamAsync`.** The process starts when enumeration begins, not when you call the method. A start failure throws `InvalidOperationException` from the first `MoveNextAsync`. Standard output and error lines arrive interleaved in the order they were written. One `ProcessStreamedOutputType.ExitCode` item ends the sequence after the redirected streams drain. Leaving the loop early, throwing from its body, or cancelling the token kills the process tree and waits for it to exit. Lines buffer without a bound while the consumer is slow.
- **`string.ToSentence()`.** It splits an identifier-style name at underscores, hyphens, spaces, case changes, and letter-digit changes. Runs of two or more capitals stay as acronyms. The rest is lowercased with the first letter capitalized: `ValueWithoutAttributes` gives `Value without attributes`, `HTMLParser` gives `HTML parser`, and `snake_case` gives `Snake case`. `null` and empty input return unchanged. It is not localized, and it does not collide with Humanizer's `string.Humanize()` when both namespaces are imported.
- **`string.MaskEnd(visibleLength, mask = "***")`.** It keeps the first `visibleLength` characters and replaces the rest with `mask`: `"device-token-123".MaskEnd(8)` gives `device-t***`. A value no longer than the prefix becomes the mask alone. `null` stays `null`. The prefix never ends inside a surrogate pair. The prefix shows the real value's start, so use it only where those characters are not sensitive. `PrefixMaskRedactor` applies it to a log classification (see [logging.md](logging.md#headlessloggingredaction)).
- **`GetDisplayName()`.** Without `[DisplayName]` or `[Display(Name)]`, it returns `ToSentence()` of the member name. An undefined enum value returns its number unchanged (`999`, `-5`).
- **`TimeUnit`.** Suffixes are `nanos`, `micros`, `ms`, `s`, `m` (minutes), `h`, and `d`. Overflow returns `false` from `TryParse` and throws from `Parse`; it never wraps.
- **`EgyptianNationalIdValidator`.** The leading digit `2` means the 1900s and `3` the 2000s; any other digit fails. The year, month, and day must form a real date, so 30 February fails.
- **`XmlHelper`.** `IsValidXml` and `IsValidXmlAsync` (each with a `string` and a `Stream` overload) check well-formedness with DTD processing ignored and no XML resolver. Inline DTDs, entity expansion, and external entities are never processed. Malformed input returns `false` instead of throwing, so the check is safe on untrusted input.

---

## Headless.Primitives

The value model on its own: the result pattern, built-in domain primitives, value objects, and paging.

### Setup

```bash
dotnet add package Headless.Primitives
```

```csharp
using Headless.Primitives;

public async Task<ApiResult<User>> GetUserAsync(Guid id, CancellationToken ct)
{
    var user = await _repo.FindAsync(id, ct);
    if (user is null)
        return ApiResult<User>.NotFound(entity: "User", key: id.ToString());

    return user; // implicit conversion from T to ApiResult<T>
}

var conflicts = ApiResult.Conflict(
    new ErrorDescriptor("user:duplicate_email", "Email already exists", ("email", email)),
    new ErrorDescriptor("user:duplicate_phone", "Phone already exists")
);

var contextualError = new ErrorDescriptor(
    "user:conflict",
    "User conflicts with existing data",
    ("email", email),
    ("tenantId", tenantId)
);

var order = new OrderBy("CreatedAt");                    // ascending by default
var descending = new OrderBy("CreatedAt", Ascending: false);
```

#### Money and MoneyAmount

```csharp
using Headless.Primitives;

var price = new Money(100m, "USD");
var withTax = price * 1.15m;          // scalar scaling -> 115.00 USD (banker's rounding)
var total = price + new Money(20m, "USD"); // same-code addition; throws on code mismatch

var amount = new MoneyAmount(9.875m).GetRounded(); // 9.88 (MidpointRounding.ToEven)
```

**Verify:** no startup signal; the package registers nothing. Assert the behavior in a unit test: `price + new Money(1m, "EUR")` throws `InvalidOperationException`, and `default(ApiResult<User>)` reports `IsSuccess` and `IsFailure` both `false`.

### API

- **Results:** `ApiResult`, `ApiResult<T>`, `Result<TValue, TError>`, `Result<TError>`, the `ApiResultError` hierarchy, `ErrorDescriptor`, `ApiResultErrorBuilder`, and `ApiResultErrorCodes`. `Match`, `Map`, and `Bind` have `…Async` forms, including overloads that pass your cancellation token to the delegate.
- **Generated primitives** (`IPrimitive<T>`): `UserId`, `AccountId`, `MoneyAmount`, `Month` (1 to 12), and `PhoneNumber`.
- **Value objects:** `Money`, `GeoCoordinate` and `FullGeoCoordinate` (range-checked latitude and longitude, Haversine distance), `Range<T>` (inclusive and exclusive containment, overlap), `PreferredLocale`, and `TimeUnit` (duration strings).
- **Paging:** `IndexPageRequest` (a sealed request with required `Index` and `Size`, so JSON and model binding create it directly) and `IndexPage<T>` (offset), `ContinuationPageRequest` (a base class: derive your query from it) and `ContinuationPage<T>` (cursor), each with `Select` and `Where` projection; `PageMetadata`, `OrderBy`, `IHasOrderByRequest`, and `IHasMultiOrderByRequest`.
- **Other:** `ExtraProperties` and `IHasExtraProperties`, `Locales` and `LocaleAttribute`, `AsyncEvent<T>`, `NameValue` and `NameValue<T>`, `File` and `Image`, and `TenantInformation`.

### Configuration

None. You construct the types directly; there is no DI registration or options.

### Design and runtime behavior

- **Results.** Factories copy the error collections you pass. A default `ApiResult` or `ApiResult<T>` is neither success nor failure: try-accessors return `false` and branch operations throw. The generic `Result<…>` types keep their guarded default-failure behavior.
- **`ErrorDescriptor`.** It takes one or more `(Key, Value)` tuples or a `ReadOnlySpan<(string Key, object? Value)>`, copies them into a case-insensitive parameter bag, and keeps no reference to your input. Use `WithParam` to add values you discover one at a time.
- **`OrderBy(string Property, bool Ascending = true)`** sorts ascending by default.
- **`PhoneNumber`.** It stores the national number as digits only, so `"555-1234"` equals `"5551234"`, and construction requires a positive country code. `ToString()` returns the raw `+{CountryCode} {Number}`, not a formatted number. Formatting, normalization, region lookup, and parsing from international format are extension members in `Headless.PhoneNumbers` (see [utilities.md](utilities.md#headlessphonenumbers)).
- **`GetProperty<T>(name)` on `IHasExtraProperties`.** It reads the value as `T` whatever CLR type the bag holds, because a bag loaded from JSON holds only what JSON can express. It converts strings (date, time, `Guid`, `TimeSpan`, and number text), `long`, `decimal`, `double`, and scalar `JsonElement` values from a bag that plain `System.Text.Json` deserialized, all with the invariant culture. A `DateTime` string keeps its kind. A `DateTimeOffset` string without an offset reads as UTC. Enum names match case-insensitively. A JSON `null` returns the default value. Use the non-generic `GetProperty(name)` only to get the stored object as it is.

---

## Headless.Urls

A fluent, mutable `Url` builder and parser, derived from Flurl's `Url` API, for projects that need URL building without the base library.

### Setup

```bash
dotnet add package Headless.Urls
```

```csharp
using Headless.Urls;

var url = Url.Parse("https://api.example.com")
    .AppendPathSegment("v1")
    .AppendPathSegments("users", "42")
    .SetQueryParam("include", "profile");

string result = url.ToString();
// https://api.example.com/v1/users/42?include=profile
```

**Verify:** no startup signal; the package registers nothing. Assert `url.ToString()` equals the expected URL in a unit test.

### API

- `Url` with `Scheme`, `Host`, `Path`, `Query`, and `Fragment` accessors.
- Parsing: `Url.Parse`, `Url.ParseQueryParams`, and `Url.ParsePathSegments`.
- Building: `AppendPathSegment`, `AppendPathSegments`, `SetQueryParam`, `AppendQueryParam`, and `RemoveQueryParam`, with `NullValueHandling` to control `null` values.
- `QueryParamCollection`: an ordered query-parameter store that keeps duplicates.

### Configuration

None. You construct `Url` directly; there is no DI registration or options.

### Design and runtime behavior

- `SetQueryParam` removes the parameter when the value is `null`, because its `nullValueHandling` defaults to `NullValueHandling.Remove`.
- `NullValueHandling` has fixed numeric values: `NameOnly = 0`, `Remove = 1`, `Ignore = 2`.
