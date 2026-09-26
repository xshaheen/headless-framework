# Headless .NET Framework — Agent Guide

Use this file to route a Headless task. Load only the domain guides that match the work; each guide is the canonical consumer contract for its packages.

Repository overview and complete package catalog: [README.md](../../README.md).

## How to use these docs

1. Resolve the exact `Headless.*` package and version from the consumer project.
2. Find the task in the router below and read the linked domain guide.
3. Follow that guide's **Agent Rules** before selecting a provider or writing setup code.
4. Verify public names and examples against the installed package or the matching source tag. `main` documents the current repository, not every released version.

Package READMEs are discovery pages. They explain why a package exists and link back to its canonical domain guide; they are not API reference.

## Framework-wide rules

- Keep provider types at the composition root. Application and domain code depend on framework abstractions such as `ICache`, `IBlobStorage`, `IBus`, or `IEmailSender`.
- Install only the packages the host uses: normally an abstractions package, the domain runtime/core package, and one provider. Some domains have a different shape; the domain guide owns that exception.
- Register each feature through its `AddHeadless*` setup builder and select providers there. Do not create a second registration path around the framework.
- Preserve explicit durability boundaries. In-memory and `*.Dev` providers are for tests, development, or intentionally ephemeral workloads; they are not production substitutes for durable providers.
- Keep cancellation tokens end-to-end and use the injected `TimeProvider` for application time. Store-backed leases, locks, and coordination use the store's clock where their guide says so.
- Treat tenancy, authorization, transactions, retries, ordering, and external side effects as domain contracts. Read every affected guide when a change crosses those boundaries.
- Prefer framework primitives before adding parallel infrastructure. Search [extensions.md](extensions.md) for guards, results, value objects, collections, IO, concurrency, and URL helpers.

## Task router

### HTTP, identity, and access control

| Task | Read |
| --- | --- |
| Bootstrap an ASP.NET Core host; Problem Details; validation; idempotency; Minimal API or MVC | [API & Web](api.md) |
| Generate OpenAPI or expose Scalar UI | [OpenAPI](openapi.md) |
| Use ASP.NET Core Identity with the Headless EF pipeline | [Identity](identity.md) |
| Resolve tenants from claims, host, route, or catalog; enforce tenant reads/writes | [Multi-tenancy](multi-tenancy.md) |
| Define and evaluate runtime permissions; integrate with ASP.NET Core authorization | [Permissions](permissions.md) |
| Protect Jobs or Messaging dashboards | [Dashboards](dashboards.md) |
| Verify reCAPTCHA or Turnstile tokens | [CAPTCHA](captcha.md) |

### Data, state, and transactions

| Task | Read |
| --- | --- |
| Use EF Core conventions, save pipelines, Couchbase, or the messaging outbox bridge | [ORM](orm.md) |
| Open and enlist an explicit transaction across EF, messaging, or jobs | [Unit of Work](unit-of-work.md) |
| Use provider-neutral SQL connections | [SQL](sql.md) |
| Cache data in memory, Redis, or hybrid L1/L2; use output cache or factory locks | [Caching](caching.md) |
| Store blobs in S3, MinIO or another S3-compatible server, Azure, R2, filesystem, Redis, or SFTP | [Blob Storage](blobs.md) |
| Persist dynamic settings | [Settings](settings.md) |
| Evaluate feature flags | [Features](features.md) |
| React when a setting, feature, or permission grant changes instead of polling | [Settings](settings.md), [Features](features.md), [Permissions](permissions.md) — each has a "Reacting to a change" section |
| Record entity changes or explicit audit events | [Audit Log](audit-log.md) |
| Issue per-tenant consecutive numbers (receipts, invoices, case numbers), gap-free when audited | [Sequences](sequences.md) |
| Grant a durable, cross-process lease that fences a stale or zombie attempt's writes; hand work to an external executor | [Fencing](fencing.md) |
| Admit a keyed operation once across retries, processes, or executors, and replay its stored result | [Idempotency](idempotency.md) |

### Distributed runtime

| Task | Read |
| --- | --- |
| Publish or consume messages; configure transports, outbox/inbox, retries, or ordering | [Messaging](messaging.md) |
| Schedule or execute background jobs and recurring work | [Jobs](jobs.md) |
| Acquire distributed locks, reader/writer locks, or semaphores | [Distributed Locks](distributed-locks.md) |
| Cap attempts per phone, email, IP, or card across replicas (OTP delivery, password reset, PIN verification) | [Rate Limiting](rate-limiting.md) |
| Track node identity, liveness, and membership | [Coordination](coordination.md) |
| Dispatch in-process requests and notifications | [Mediator](mediator.md) |

### Integration services

| Task | Read |
| --- | --- |
| Send email | [Email](emails.md) |
| Send SMS | [SMS](sms.md) |
| Send push notifications | [Push Notifications](push-notifications.md) |
| Process Paymob cash-in, cash-out, or service operations | [Payments](payments.md) |
| Implement resumable TUS uploads | [TUS](tus.md) |
| Resize, encode, or inspect images | [Imaging](imaging.md) |
| Extract text from documents for indexing | [Media Indexing](media.md) |

### Foundations and tooling

| Task | Read |
| --- | --- |
| Use current-user/locale/time-zone services, guards, DDD entities, or local domain events | [Core](core.md) |
| Encrypt strings, build a blind index, or hash and verify secrets (PINs, API-key secrets, recovery codes, passwords outside Identity) | [Security](security.md) |
| Find a primitive, result type, collection helper, concurrency helper, IO helper, or URL builder | [Extensions and Primitives](extensions.md) |
| Serialize JSON or MessagePack behind `ISerializer` | [Serialization](serialization.md) |
| Configure Serilog defaults | [Logging](logging.md) |
| Use hosting helpers, validators, source-generated primitives, Redis scripts, geospatial helpers, sitemaps, or slugs | [Utilities](utilities.md) |
| Write unit/integration tests or use Testcontainers and the messaging harness | [Testing](testing.md) |

## Choosing a coordination primitive

Four primitives answer four different questions. Choose by the question, not by the words "lock" or "lease".

| Question | Primitive | Held as | What refuses a stale holder |
| --- | --- | --- | --- |
| May this process run now? | Distributed lock: `IDistributedLock` ([Distributed Locks](distributed-locks.md)) | A live in-process handle (`IDistributedLease`). It cannot be handed to another process. | Nothing by default. The protected resource must check `FencingToken`, or the lock must be transaction-coupled. |
| Who owns this work, at which generation, until when? | Fenced lease: `IFencedLeases`, `unit.Leases` ([Fencing](fencing.md)) | A durable row that any process carries as `(resource, Generation)`. | The database, inside the writer's transaction: `unit.Leases.FenceAsync`. |
| Has this operation already happened, and what was its result? | Idempotent admission: `IIdempotentOperations`, `unit.Idempotency`; `Headless.Api.Idempotency` for HTTP ([Idempotency](idempotency.md)) | A record per tenant-scoped key and request fingerprint, with its own lease and generation. | The database: `CompleteAsync` and `unit.Idempotency.FenceAsync` refuse a superseded generation. |
| Which node incarnations are alive? | Membership: `INodeMembership` ([Coordination](coordination.md)) | A `NodeIdentity` (`node@incarnation`) that the consumer stamps on its own rows. | The consumer's recovery write, guarded by owner identity. Membership owns nothing. |

### Why each primitive exists

- **Distributed lock.** Cheap mutual exclusion while the holder is alive. Use it to avoid duplicate work (efficiency), or take a transaction-coupled lock whose lifetime is the transaction. Redis locks expire by TTL. PostgreSQL and SQL Server locks are session-scoped advisory or application locks with no TTL: they live as long as the holding connection.
- **Fenced lease.** A lock handle dies with its process and connection. A fenced lease is a database row instead, so an external executor can hold it, renew it, and post a result hours later. Expiry uses the database clock, and `SweepExpiredAsync` hands abandoned work to a durable handler (`unit.Outbox`, `unit.Jobs`) in the same transaction that marks the lease abandoned.
- **Idempotent admission.** Retries must replay a result, not repeat the operation. `AdmitAsync` returns `Admitted`, `InFlight`, `Replay`, or `Conflict` for one tenant-scoped key and fingerprint, and the stored result replays from any entry point. The record carries its own lease and generation; it does not use the Fencing table. It answers "once per key", not "one holder at a time per resource".
- **Membership.** Recovery needs to know which process runs died. Membership reports liveness only and never records ownership. Consumers stamp `node@incarnation` on their own rows and reclaim rows whose owner is no longer live.

Jobs and Messaging need none of these for their own rows. Each work row carries its lease in its own columns (Jobs: `OwnerId` and `LockedUntil`; Messaging: `Owner` and `LockedUntil`), claimed and renewed by guarded updates on the database clock. That is correct because the row is the work: the claim, the renewal, and the result update the same row, so there is no second table and no lock order. Follow that pattern when your work already has a row you own. Use a fenced lease when it does not, or when an external executor needs a generation to carry.

### Fence, token, and lease

- **Lease:** time-bounded ownership that ends unless renewed. Expiry alone never stops a paused holder from writing after it resumes.
- **Token:** a number that identifies one grant: `IDistributedLease.FencingToken`, `FencedLease.Generation`, `IdempotentAdmission.Generation`, `NodeIncarnation`. A token protects nothing until something compares it.
- **Fence:** the comparison that refuses a write carrying a superseded token, done atomically with that write. Fencing and Idempotency fence in the database inside your transaction. A lock's `FencingToken` is fenced only if your resource stores the last accepted token and rejects anything not greater than it.

**A lock alone never protects data.** A holder can pause (garbage collection, a network partition), lose its lock or lease, and resume writing without knowing. Protect a correctness invariant with a fence at the write: a transaction-coupled lock, a fenced lease, an admission generation, or a guarded row update. No fence recalls a side effect already made outside the database. Make such a side effect idempotent at its own boundary, or trigger it from the committed row.

## Cross-domain changes

Load every row touched by the behavior, not just the package being edited:

| Change | Guides |
| --- | --- |
| Tenant-aware HTTP request | [API](api.md), [Multi-tenancy](multi-tenancy.md), and [Permissions](permissions.md) when authorization is involved |
| Atomic database write plus message or job | [ORM](orm.md), [Unit of Work](unit-of-work.md), and [Messaging](messaging.md) or [Jobs](jobs.md) |
| Dashboard exposure | Owning [Jobs](jobs.md) or [Messaging](messaging.md) guide plus [Dashboards](dashboards.md) |
| Provider-backed integration test | Owning domain plus [Testing](testing.md) |

## When the docs and code disagree

Treat the installed package or source at the consumer's resolved version as the executable contract. Report the mismatch and update the canonical domain guide; do not copy the correction into package READMEs. For documentation changes, follow [the authoring contract](../authoring/AUTHORING.md).
