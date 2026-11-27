# Headless .NET Framework — Agent Guide

Route a task in an application that uses Headless to the guide that owns it. Each domain guide is the consumer contract for its packages: how to choose, wire, call, observe, verify, and test them.

The package catalog is in [README.md](../../README.md).

## How to use these docs

1. Read the exact `Headless.*` packages and versions from the application's project files.
2. Find the task in the [task router](#task-router) and read the linked guide's Orientation and Agent Rules before you pick a provider or write setup code.
3. If the guide has a **Topics** table, read only the topic files whose "Read when" row matches the task.
4. Finish each setup recipe with its **Verify** step. The wiring is done when that check passes, not when the code compiles.
5. Check public names against the installed package. These docs describe `main`, which can be ahead of the version the application references.

Package READMEs only route to these guides; they are not API reference.

## Framework-wide rules

- Keep provider types at the composition root. Application and domain code depend on abstractions such as `ICache`, `IBlobStorage`, `IBus`, or `IEmailSender`.
- Install only the packages the host uses: usually an abstractions package, the domain's root package, and one provider. A guide whose domain has another shape says so.
- Register each feature through its `AddHeadless*` setup builder and pick providers there. Do not register the feature's services a second way around the builder.
- Register the relational database once with `AddPostgreSqlSql` or `AddSqlServerSql`, then call each storage feature's parameterless `UsePostgreSql()` or `UseSqlServer()`. Every feature then shares the connection and creates its tables in the `headless` schema. [SQL](sql.md#shared-connection-and-schema-for-storage-features) owns the overrides.
- Create tables, broker topology, and seed data as deploy steps, not at startup: run `resources provision` and `seed`, or `deploy` for both. The host only verifies those resources when it starts. Run `check` before you route traffic to a new build. [Utilities](utilities.md#resource-provisioning) owns the verbs.
- When environments share one Redis, give each host a `Headless:Prefix`. Every key-value feature puts it in front of its names. Relational storage and blob containers separate environments by database, schema, or bucket instead. [Utilities](utilities.md#isolation-prefix) owns the contract and how to [prefix your own names](utilities.md#prefix-your-own-names) with `HeadlessPrefixOptions.Apply`.
- Expect a readiness health check, tagged `ready` and `headless`, from each provider package with an external dependency. It stays off the liveness endpoint. Remove one with `RemoveHealthChecks`. [Utilities](utilities.md#health-checks) owns the list.
- Use in-memory and `*.Dev` providers only for tests, development, or work you accept losing. They do not replace durable providers in production.
- Pass cancellation tokens end to end.
- Use the registered `TimeProvider` for times the application records and for schedules a test drives by advancing a fake clock: due times, leases, and retention. Waits that ride out a failure (a retry back-off, a reconnect, an I/O timeout) run on `TimeProvider.System`, so a host whose fake clock never advances still recovers. Two exceptions change how you write tests:
  - Messaging takes its back-off waits from a separate clock registration (`MessagingOptions.BackoffClockServiceKey`, default `TimeProvider.System`), so a test can step through retries with its own fake clock. [Messaging](messaging.md) owns the list of waits.
  - The relational stores of Sequences, Fencing, Idempotency, and Coordination wait out a deadlock on the registered clock, so a test with a fake clock advances it to let the retry run.
- For your own reconnect loop, use `ReconnectBackoff` (`Headless.Threading`). It runs on the system clock unless you pass another.
- Treat tenancy, authorization, transactions, retries, ordering, and external side effects as contracts owned by their guides. When a change crosses one of them, read every guide it touches.
- Before you write a helper, search [Extensions](extensions.md) for guards, results, value objects, collections, IO, concurrency, and URL helpers.

## Task router

### HTTP, identity, and access control

| Task | Read |
| --- | --- |
| Bootstrap an ASP.NET Core host; Problem Details; validation; Minimal API or MVC; JWT, ASP.NET Core Identity, or User-Agent parsing | [API & Web](api.md) |
| Generate OpenAPI or expose Scalar UI | [OpenAPI](openapi.md) |
| Use ASP.NET Core Identity with the Headless EF pipeline | [Identity](identity.md) |
| Resolve tenants from claims, host, route, or catalog; enforce tenant reads/writes | [Multi-tenancy](multi-tenancy.md) |
| Define and evaluate runtime permissions; integrate with ASP.NET Core authorization | [Permissions](permissions.md) |
| Serve the operations dashboard shell, write a dashboard module, add built-in modules (Jobs, Messaging, Cluster, Leases, Rate limits, Idempotency, Sequences, Dev inbox), or protect a dashboard a host writes | [Dashboards](dashboards.md) |
| Verify reCAPTCHA or Turnstile tokens | [CAPTCHA](captcha.md) |

### Data, state, and transactions

| Task | Read |
| --- | --- |
| Use EF Core conventions, save pipelines, Couchbase, or the messaging outbox bridge | [ORM](orm.md) |
| Open and enlist an explicit transaction across EF, messaging, or jobs, or flag at build time a write made outside the unit in scope | [Unit of Work](unit-of-work.md) |
| Use provider-neutral SQL connections, or share one connection and schema across storage features | [SQL](sql.md) |
| Cache data in memory, Redis, or hybrid L1/L2; use output cache or factory locks | [Caching](caching.md) |
| Store blobs in S3, MinIO or another S3-compatible server, Azure, R2, filesystem, Redis, or SFTP | [Blob Storage](blobs.md) |
| Persist dynamic settings | [Settings](settings.md) |
| Evaluate feature flags | [Features](features.md) |
| Gate a controller action or Minimal API endpoint on a feature flag | [Features](features.md#gating-http-endpoints) |
| React when a setting, feature, or permission grant changes instead of polling | [Settings](settings.md), [Features](features.md), [Permissions](permissions.md) — each has a "Reacting to a change" section |
| Record entity changes or explicit audit events | [Audit Log](audit-log.md) |
| Issue per-tenant consecutive numbers (receipts, invoices, case numbers), gap-free when audited | [Sequences](sequences.md) |
| Search Arabic names and text so hamza, tashkeel, teh marbuta, alef maksura, and Persian or Urdu spellings match, through a PostgreSQL trigram index | [Search](search.md) |
| Grant a durable, cross-process lease that fences a stale or zombie attempt's writes; hand work to an external executor | [Fencing](fencing.md) |
| Admit a keyed operation once across retries, processes, or executors, and replay its stored result; replay HTTP responses for `Idempotency-Key` retries | [Idempotency](idempotency.md) |

### Distributed runtime

| Task | Read |
| --- | --- |
| Publish or consume messages; declare consumers and message contracts; tune consumers; configure transports, outbox/inbox, retries, or ordering | [Messaging](messaging.md) |
| Schedule or execute background jobs and recurring work; set job retries, report job progress, or requeue failed jobs | [Jobs](jobs.md) |
| Define a failure policy: immediate and delayed retries, backoff, and exceptions that end a failure at once | [Reliability](reliability.md) |
| Acquire distributed locks, reader/writer locks, or semaphores, or fence stale writes with fencing tokens | [Distributed Locks](distributed-locks.md) |
| Cap attempts per phone, email, IP, or card across replicas (OTP delivery, password reset, PIN verification) | [Rate Limiting](rate-limiting.md) |
| Track node identity, liveness, and membership, or show a node's membership history and cluster settings | [Coordination](coordination.md) |
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
| Find the country, city, or network owner of an IP address | [IP Geolocation](ip-geolocation.md) |
| Extract text from documents for indexing | [Media Indexing](media.md) |

### Foundations and tooling

| Task | Read |
| --- | --- |
| Use guards, DDD entities, or local domain events | [Core](core.md) |
| Use current-user, locale, time-zone, correlation, cancellation, or host-identity services | [Context](context.md) |
| Encrypt personal data per field under per-tenant keys, find rows by an encrypted value through a blind index, rotate data or key-encryption keys, or crypto-shred a tenant | [Encryption](encryption.md) |
| Hash and verify secrets (PINs, API-key secrets, recovery codes, passwords outside Identity) | [Hashing](hashing.md) |
| Find a primitive, result type, collection helper, concurrency helper, IO helper, or URL builder | [Extensions and Primitives](extensions.md) |
| Serialize JSON or MessagePack behind `ISerializer` | [Serialization](serialization.md) |
| Configure host logging: console format, enrichment on every entry, levels, a startup crash logger | [Logging](logging.md#headlesslogging) |
| Keep secrets and personal data out of logs (data classification, redactors) | [Logging](logging.md) |
| Keep credential paths out of HTTP logging and spans, classify HTTP logging fields | [Logging](logging.md#headlessloggingredactionaspnetcore) |
| Use hosting helpers, validators, health checks, the on-demand health check monitor and its probe history, the host manifest (`describe` command), the pre-start health check (`check` command), background warm-up behind readiness, source-generated primitives, Redis scripts, geospatial helpers, sitemaps, or slugs | [Utilities](utilities.md) |
| Write unit/integration tests or use Testcontainers and the messaging harness | [Testing](testing.md) |

## Choosing a coordination primitive

Choose by the question, not by the words "lock" or "lease". [Fencing § Choosing a coordination primitive](fencing.md#choosing-a-coordination-primitive) owns the full comparison: what each primitive holds, what refuses a stale holder, and how a fence differs from a token and a lease.

| Question | Use | Guide |
| --- | --- | --- |
| May this process run now? | Distributed lock (`IDistributedLock`) | [Distributed Locks](distributed-locks.md) |
| Who owns this work, at which generation, until when? | Fenced lease (`IFencedLeases`, `unit.Leases`) | [Fencing](fencing.md) |
| Has this operation already happened, and what was its result? | Idempotent admission (`IIdempotentOperations`, `unit.Idempotency`; `Headless.Idempotency.ResponseReplay` for HTTP) | [Idempotency](idempotency.md) |
| Which node incarnations are alive? | Membership (`INodeMembership`) | [Coordination](coordination.md) |
| Does the work already have a row you own? | A lease in that row's own columns, claimed and renewed by guarded updates, as Jobs and Messaging do | [Fencing § Work that already has a row](fencing.md#work-that-already-has-a-row) |

**A lock alone never protects data.** A holder can pause, lose its lock or lease, and resume writing without knowing. Protect a correctness invariant with a fence at the write: a transaction-coupled lock, a fenced lease, an admission generation, or a guarded row update. No fence recalls a side effect already made outside the database; make it idempotent at its own boundary, or trigger it from the committed row.

## Cross-domain changes

Load every row touched by the behavior, not just the package being edited:

| Change | Guides |
| --- | --- |
| Tenant-aware HTTP request | [API](api.md), [Multi-tenancy](multi-tenancy.md), and [Permissions](permissions.md) when authorization is involved |
| Atomic database write plus message or job | [ORM](orm.md), [Unit of Work](unit-of-work.md), and [Messaging](messaging.md) or [Jobs](jobs.md) |
| Dashboard exposure or a feature dashboard module | Owning domain guide, such as [Jobs](jobs.md) or [Messaging](messaging.md), plus [Dashboards](dashboards.md) |
| Provider-backed integration test | Owning domain plus [Testing](testing.md) |

## When the docs and code disagree

Treat the installed package or source at the consumer's resolved version as the executable contract. Report the mismatch and update the canonical domain guide; do not copy the correction into package READMEs. Contributors changing these guides follow [the repository's documentation rules](../../AGENTS.md#documentation).
