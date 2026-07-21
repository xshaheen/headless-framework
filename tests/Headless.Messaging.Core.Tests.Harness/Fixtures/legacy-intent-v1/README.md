# Legacy intent v1 compatibility evidence

These immutable fragments are derived from the released `0.11.0` source, whose annotated tag resolves to commit `2594ecfdd8038dd748ef8681887f79028aa30f14`. They characterize the compatibility contract before `MessageLane` existed; no fixture was produced by the new implementation.

## Source provenance

| Evidence | Released source | Git blob |
|---|---|---|
| `Bus = 0`, `Queue = 1`, `short` backing | `src/Headless.Messaging.Abstractions/IntentType.cs` | `87ea762039f7b1309cef3b2f815c7c33f81b4906` |
| Header key `headless-intent` | `src/Headless.Messaging.Abstractions/Headers.cs` | `8e388a8f89b9186fc44df800fdb5ac01b1ac824c` |
| Header values emitted through `intentType.ToString()` | `src/Headless.Messaging.Core/Internal/IMessagePublishRequestFactory.cs` | `038d225f1aacd10086ea47191f36fb0dfd20bd44` |
| PostgreSQL `SMALLINT` columns | `src/Headless.Messaging.Storage.PostgreSql/PostgreSqlStorageInitializer.cs` | `0fc4becca5b5c5fa9b2022e4b4c3a7754114fd7a` |
| SQL Server `smallint` columns | `src/Headless.Messaging.Storage.SqlServer/SqlServerStorageInitializer.cs` | `e918553e0e114cd38c03c43a76d3a43e4da22b03` |
| In-memory rows retain `IntentType` directly | `src/Headless.Messaging.InMemoryStorage/InMemoryDataStorage.cs` | `4d1d53bba0f1ce505235333e0cc46c59e27654ea` |

`transport-intent.headers` is the exact UTF-8 key/value output implied by the released header constant, enum names, and writer assignment. The SQL files contain only the exact released column literals; they are schema evidence, not fabricated row captures.

The storage conformance tests complement these static fragments by writing and reading both exact numeric values through real PostgreSQL, SQL Server, and in-memory providers. Provider-specific broker envelope capture remains in the real transport integration leaves; this directory intentionally does not claim synthetic broker frames as production evidence.

To verify provenance, use `git rev-parse 0.11.0^{commit}` and `git rev-parse 0.11.0:<source-path>` and compare the results above.
