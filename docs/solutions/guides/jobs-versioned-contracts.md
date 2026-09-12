# Jobs contract storage

Applications initialize their Jobs database from the current EF model before starting workers, schedulers, or definition writers. Headless provides mappings; it does not initialize or alter the application's schema at runtime. Generate the initial schema from the application's actual DbContext, provider, custom entities, table names, and schema.

## Executable identity

Every executable row owns `(Function, ContractVersion, Request bytes)`. A materialized cron occurrence copies the definition tuple under its write lock and retains its exact bytes across retries and restarts. Later definition edits do not change an existing occurrence's intent. Contract versions describe payload schemas independently of retries, leases, attempts, and business-key generations.

`JobContract.InitialVersion` is `"1"`, the default for a newly declared contract. Declare another version when the payload schema changes. This CLR default is not a database default: every writer must supply a version.

| Entity | Stored contract and context |
| --- | --- |
| `TimeJobEntity` | Required function/version, nullable request bytes, correlation, causation, and tenant |
| `CronJobEntity` | Required function/version, nullable request bytes and lineage; definition remains system scope |
| `CronJobOccurrenceEntity` | Its own required function/version, nullable exact request bytes, correlation, causation, and tenant; materialized cron occurrences remain system scope |

## Identity rules and mappings

`Function` contains 1–200 UTF-16 code units; `ContractVersion` contains 1–100. Both compare ordinally and case-sensitively. Leading/trailing whitespace, control characters, invalid surrogate sequences, normalization, trimming, and truncation are rejected. Embedded ordinary spaces are permitted. `Invoice.Send` and `invoice.send`, and `V1` and `v1`, are different identities.

PostgreSQL uses `varchar(200)` / `varchar(100)` with `COLLATE "C"`; SQL Server uses `nvarchar(200)` / `nvarchar(100)` with `COLLATE Latin1_General_100_BIN2`. PostgreSQL counts Unicode code points in its physical length bound; the runtime contract counts UTF-16 units. For example, 100 rocket characters fill a function name and 101 must fail on both providers. Rejecting edge whitespace also prevents aliases from SQL Server's padded string comparison.

EF converters validate normal writes. Database columns are required and bounded, but `NOT NULL` does not reject blank strings or implement the full Unicode contract. Raw SQL and custom writers must perform the same validation. Constructing a `JobFunctionDescriptor` validates a name/version without registering or executing it.

## Verification

The PostgreSQL and SQL Server claim conformance suites create tables from the production EF mappings. They verify ordinal identity, UTF-16 boundaries, requestless round trips, required versions without database defaults, invalid new writes, and occurrence payload preservation after parent edits and restart. Custom providers must retain these guarantees and reject unsupported executable versions before deserializing payloads.

See [keyed scheduling storage](jobs-keyed-scheduling.md) for key generations and database constraints.
