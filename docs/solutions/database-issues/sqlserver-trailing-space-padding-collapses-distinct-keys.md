---
title: "SQL Server ANSI_PADDING collapses trailing-space keys that PostgreSQL keeps distinct"
date: 2026-09-25
category: database-issues
module: Headless.Sequences
problem_type: database_issue
component: database
severity: high
symptoms:
  - "A fast-mode increment on key 'invoice ' (trailing space) incremented the gap-free 'invoice' row outside any unit of work"
  - "Two tenant ids differing only by a trailing space shared one counter row on SQL Server but stayed distinct on PostgreSQL"
  - "SequenceRequestResolver's ordinal policy-dictionary lookup for a padded key resolved to a different policy than the row SQL Server's PK comparison matched"
  - "The collision reproduced under every SQL Server collation tried, including binary _BIN2 collations"
  - "A DATALENGTH-qualified equality predicate still collided on the clustered primary key, so predicate changes alone could not fix it"
root_cause: missing_validation
resolution_type: code_fix
related_components:
  - service_class
  - database
tags:
  - sequences
  - sql-server
  - ansi-padding
  - trailing-space
  - collation
  - cross-provider
  - key-validation
  - provider-parity
---
## Problem

On SQL Server, `=` and primary-key/unique-index uniqueness pad the shorter string with trailing spaces before comparing. This happens under every collation, `_BIN2` included. So `'acme'` and `'acme '` count as one key on SQL Server. On PostgreSQL (`varchar COLLATE "C"`) they are two keys. In `Headless.Sequences` as first written, .NET code and the PostgreSQL provider treated the two as different keys, and the SQL Server provider treated them as one row. The providers disagreed and nothing reported it. Code review of PR #973 found this before merge. Two reviewers reported it independently: the in-process correctness reviewer and a cross-model Codex adversarial reviewer.

## Symptoms

- Policy lookup is ordinal. `SequencesOptions.Policies` is a `Dictionary<string, SequencePolicy>(StringComparer.Ordinal)` (`src/Headless.Sequences.Core/SequencesOptions.cs:18-19`), and `GetPolicy` does an exact lookup (`:21-23`). So `"invoice"` could be configured as GapFree while `"invoice "` fell back to the Fast default.
- The SQL Server increment is `WHERE tenant_id = @TenantId AND name = @Name AND partition = @Partition` (`src/Headless.Sequences.SqlServer/SqlServerSequenceStore.cs:210-212`), which matches the padded and unpadded name to the same row. A Fast call on `"invoice "` therefore incremented the GapFree counter outside any unit of work, leaving a gap. It also blocked on the `UPDLOCK, HOLDLOCK` row lock held by a gap-free unit on `"invoice"`.
- Tenant ids that differed only by a trailing space (`"acme"` vs `"acme "`) shared one counter on SQL Server and kept separate counters on PostgreSQL.
- The `KeyCollation` comment used to say `Latin1_General_100_BIN2` makes names, partitions, and tenant ids "match ordinally (case- and accent-sensitive) ... the same as the PostgreSQL provider". That was false for trailing spaces. The comment has since been corrected to state that `_BIN2` does not stop padding and that refusing padded key parts is what makes matching ordinal. The padding rule is also documented on `SequenceKeyText` (`src/Headless.Sequences.Core/SequenceKeyText.cs:8-13`).

## What Didn't Work

- **A `_BIN2` collation.** Binary collations change case and accent sensitivity and ordering. They do not turn off ANSI trailing-space padding in comparisons. The table already declares every key column `COLLATE {KeyCollation}` (`src/Headless.Sequences.SqlServer/SqlServerSequencesStorageInitializer.cs:71-73`), and the collision happened anyway.
- **A `DATALENGTH`-qualified WHERE predicate** (for example, `AND DATALENGTH(name) = DATALENGTH(@Name)`). It would stop the `UPDATE` from matching the padded key. But the insert branch then adds a second row that the clustered primary key over `(tenant_id, name, partition)` (`SqlServerSequencesStorageInitializer.cs:77-81`) considers a duplicate, because the index uses the same padded comparison. The result is a PK violation instead of a separate counter. That PK is load-bearing and cannot be widened: `HOLDLOCK` takes its key-range lock on that index to serialize concurrent first calls (`SqlServerSequencesStorageInitializer.cs:48-50`).
- **`SET ANSI_PADDING`.** It controls how `char`/`varchar` values are *stored*, not how they are compared. It does not apply to `nvarchar` at all ([SET ANSI_PADDING](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-ansi-padding-transact-sql)). The increment batch also avoids session `SET` options on purpose, because they would leak into the caller's gap-free session (`SqlServerSequenceStore.cs:200-202`).

## Solution

Refuse any key part that starts or ends with whitespace, on every provider, before any statement runs. This keeps every key part ordinal on both databases.

- `SequenceKeyText` (`src/Headless.Sequences.Core/SequenceKeyText.cs:14-17`) implements `HasSurroundingWhitespace` (checks the first and last characters with `char.IsWhiteSpace`) and `EnsureNoSurroundingWhitespace` (`:21-31`), which throws `ArgumentException`.
- `SequenceRequestResolver.Resolve` is the one resolver both entry points use: `SequenceGenerator` (`src/Headless.Sequences.Core/SequenceGenerator.cs:16,31`) and `UnitOfWorkSequencesFeature` (`src/Headless.Sequences.Core/UnitOfWorkSequencesFeature.cs:27`). It applies the check to the name (`src/Headless.Sequences.Core/SequenceRequestResolver.cs:18`), the partition (`:45`), and the current tenant id (`:78`).
- Configuration gets the same check, so a padded policy name cannot sit in the ordinal dictionary unreachable:
  - `HeadlessSequencesSetupBuilder.Policy` (`src/Headless.Sequences.Core/HeadlessSequencesSetupBuilder.cs:73`)
  - the options validator's `Policies` key rule (`src/Headless.Sequences.Core/SequencesOptions.cs:41-42`)
- Tests:
  - `should_refuse_a_key_part_padded_with_whitespace` (`tests/Headless.Sequences.Core.Tests.Unit/SequenceGeneratorTests.cs:203-222`) covers leading and trailing padding, including a tab, on the name, partition, and tenant id. It asserts that the store received no calls.
  - The conformance case `should_reject_invalid_key_parts_before_any_statement` (`tests/Headless.Sequences.Tests.Harness/SequencesConformanceTests.cs:255-299`) runs against both providers. It checks that a padded name, partition, and tenant (`"acme "`) are refused and that no row was written.

## Why This Works

SQL Server follows ANSI SQL-92 §8.2 (General Rule 3): before comparing two character strings, it pads the shorter one with spaces, so `'abc' = 'abc '` is true. `LIKE` is the only documented exception ([= (String comparison or assignment)](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/string-comparison-assignment); KB [INF: How SQL Server Compares Strings with Trailing Spaces](https://support.microsoft.com/en-us/topic/inf-how-sql-server-compares-strings-with-trailing-spaces-b62b1a2d-27d3-4260-216d-a605719003b0)). The same documented behavior shows up in `LEN`, which excludes trailing spaces, while `DATALENGTH` counts them.

Uniqueness uses the same comparison. That is why no predicate-level fix can keep `'a'` and `'a '` as separate rows under the existing PK. The only change that works on both providers is to never let such a pair exist. Once no key part has trailing whitespace, padded comparison and ordinal comparison give the same answer, and `_BIN2` plus the ordinal dictionary behave the same as PostgreSQL's `COLLATE "C"`.

The rule also refuses leading whitespace and non-space whitespace, even though SQL Server pads only U+0020. This keeps the rule symmetric and simple: nobody needs a real key that begins or ends with whitespace, and refusing it costs nothing.

## Prevention

- Validate key text in the single resolver that every entry point shares, and in every place that configures keys (the builder method and the options validator). Do not validate in each store.
- Keep the conformance case asserting that padded keys are refused. Because it runs against every provider, a new provider or a refactored resolver cannot quietly bring the divergence back.
- Do not rely on "BIN2 = ordinal". A `_BIN2` collation gives case- and accent-sensitive code-point comparison and nothing more. The `KeyCollation` comment (`src/Headless.Sequences.SqlServer/SqlServerSequencesSchema.cs:15-18`) now says so.
- Other SQL Server stores in this repo that key rows by caller strings are **worth checking**. This is not verified as a bug; no divergent behavior was reproduced:
  - Settings: `CREATE UNIQUE ... ([Name])` and `([Name], [ProviderName], [ProviderKey])` (`src/Headless.Settings.Storage.SqlServer/SqlServerSettingsStorageInitializer.cs:132,145`), plus a `NULL`-filtered sibling index on the remaining columns for the no-provider-key scope (`:152`).
  - Features: unique on `[Name]` and on `([Name], [ProviderName], [ProviderKey])` (`src/Headless.Features.Storage.SqlServer/SqlServerFeaturesStorageInitializer.cs:145,161,177`), plus a `NULL`-filtered sibling index on the remaining columns (`:185`).
  - Permissions: unique on `[Name]` and on `([TenantId], [Name], [ProviderName], [ProviderKey])` (`src/Headless.Permissions.Storage.SqlServer/SqlServerPermissionsStorageInitializer.cs:75,110,146`), plus a `NULL`-filtered sibling index for the host scope (`:156`).
  - These columns declare no `COLLATE`, so they use the database default collation. Their entity constructors, and the permission grant store's own parameter checks, reject only null, empty, or whitespace-only values, not padded ones. See `src/Headless.Settings.Core/Entities/SettingValueRecord.cs:44-46`, `src/Headless.Features.Core/Entities/FeatureValueRecord.cs:40-42`, `src/Headless.Permissions.Core/Entities/PermissionGrantRecord.cs:92-94`, and `src/Headless.Permissions.Core/Grants/PermissionGrantStore.cs:556-557`.
  - So a `ProviderKey` or `TenantId` of `"acme "` next to `"acme"` would hit the padded-uniqueness rule on SQL Server. Whether that makes SQL Server and PostgreSQL behave differently depends on the PostgreSQL sibling's column collations and on how upstream callers normalize these values. Neither was checked here.

## Related

- `docs/solutions/guides/jobs-versioned-contracts.md` and `docs/solutions/guides/jobs-keyed-scheduling.md` apply the same rule to Jobs identities (function name, contract version, business key): `COLLATE "C"` / `Latin1_General_100_BIN2` plus refusing edge whitespace.
- `docs/solutions/design-patterns/relational-counter-table-over-native-database-sequences.md` covers the Sequences table these keys live in.

