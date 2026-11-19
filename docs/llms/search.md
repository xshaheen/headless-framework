---
domain: Search
packages: Search, Search.PostgreSql, Search.PostgreSql.EntityFramework
---

# Search

> Arabic search keys. A versioned, closed rule table folds hamza seats, alef, teh marbuta, alef maksura, and Persian, Urdu, and Kurdish letter variants; deletes tashkeel, tatweel, and Quranic marks; folds Arabic presentation forms and accented Latin letters; and turns everything else into a word separator. PostgreSQL stores the key in a generated column with a trigram index and normalizes the query term with the same function, so `احمد` finds `أحمد`, `فاطمه` finds `فاطمة`, and `محمدى` finds `محمدي` through an index. The same rules run in C# for highlighting and in-memory work.

## Orientation

`Headless.Search.PostgreSql` ships the database function and needs no EF Core. `Headless.Search.PostgreSql.EntityFramework` adds the EF Core model helpers on top of it. The default path:

```csharp
// Ship the database function: resources provision creates it before any EF Core migration runs.
builder.Services.AddHeadlessSearch(setup => setup.UsePostgreSql(connectionString));

// In the DbContext: one rule version per model, then a stored key per searchable column.
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasHeadlessArabicSearch(ArabicSearchVersion.V1);
    modelBuilder.Entity<Person>(entity => entity.HasArabicSearchKey(p => p.Name, p => p.NameSearch));
}

// Query: the database normalizes the term with the function that built the stored keys.
// A term with no searchable character ("!!!", only tashkeel, null) matches no rows.
var people = await db.People
    .WhereArabicSearchMatches(p => p.NameSearch, term)
    .ToListAsync(ct);
```

`HasArabicSearchKey` maps `NameSearch` to `GENERATED ALWAYS AS ("headless".ar_norm_v1("Name")) STORED` with a GIN `gin_trgm_ops` index. `HasHeadlessArabicSearch` declares the `pg_trgm` extension. The next scaffolded migration creates all three. `Name` keeps the original text for display. The application never writes `NameSearch`.

## Agent Rules

- Match in the database, never in C#. Query with `WhereArabicSearchMatches(p => p.Key, term)`, which normalizes the term with the database function that built the stored keys. Use `ArabicSearchNormalizer` only for highlighting and in-memory filtering, because a term built in C# can silently disagree with the stored keys.
- Use `WhereArabicSearchMatches`, not a hand-written `EF.Functions.Like(key, "%" + ArabicSearchFunctions.Key(term) + "%")`. The hand-written form matches every row for a term whose key is empty (`"!!!"`, only tashkeel, `null`), because EF Core turns it into `LIKE '%%'`. The helper adds the guard.
- Never use `string.Contains` on a key. Npgsql translates it to `strpos`, which the trigram index cannot serve.
- Add no LIKE escaping. A normalized term holds only alphabet characters and single spaces, never `%`, `_`, or `\`.
- Require a minimum term length where the table is large. A term under three characters gives the trigram index nothing to look up, so the query scans. The helper enforces no minimum.
- Pass the same version everywhere. `HasHeadlessArabicSearch(version)` fixes it for the whole model, and `ArabicSearchNormalizer.Normalize` takes it with no default.
- Call `HasHeadlessArabicSearch` before you configure any entity that calls `HasArabicSearchKey`. Without it, `HasArabicSearchKey` throws `InvalidOperationException`.
- Configure `HasColumnName` on the source property before `HasArabicSearchKey`, or pass `sourceColumn`. `HasArabicSearchKey` reads the source column name when it is called, so a later rename is missed.
- Pass `HasHeadlessArabicSearch(version, schema)` the same schema as `PostgreSqlSearchOptions.Schema`. Both default to `headless`.
- Never `CREATE OR REPLACE` a search function, and never edit a released rule. A stored key keeps the value its function computed at write time, so a replaced function makes stored keys disagree with new query terms, with no error. A rule change ships as a new version and a new function.

## Core Concepts

### The rule table

`ArabicSearchRuleSet.V1` is the whole contract: an output alphabet, one-to-one mappings, expansions, and deletions. A key is built in four steps:

1. Each expansion character becomes its string: the lam-alef ligatures `ﻻ ﻷ ﻹ ﻵ` (and their final forms) become `لا`, `ﷲ` becomes `الله`, and `æ œ ĳ þ ß` become `ae oe ij th ss`.
2. Each mapped character becomes its target, and each deleted character is removed without a space.
3. Every run of characters outside the alphabet becomes one space. Whitespace, punctuation, hyphens, apostrophes, symbols, emoji, and every other script are separators, so `"عبد-الله"` and `"عبد الله"` share a key.
4. Leading and trailing spaces are trimmed. `null` stays `null`.

| Group | V1 rules |
| --- | --- |
| Alphabet | `0-9`, `a-z`, and the 29 base Arabic letters `ء ا ب ت ث ج ح خ د ذ ر ز س ش ص ض ط ظ ع غ ف ق ك ل م ن ه و ي` |
| Alef | `آ أ إ ٱ ٲ ٳ ٵ` → `ا` |
| Waw | `ؤ ٶ ٷ` and `ۄ ۅ ۆ ۇ ۈ ۉ ۊ ۋ ۏ` → `و` |
| Yeh | `ى ئ ی ے ۓ ٸ` and `ؠ ؽ ؾ ؿ ۍ ێ ې ۑ` → `ي` |
| Heh | `ة ھ ہ ۀ ۂ ۃ ە ۿ` → `ه` |
| Kaf | `ک ڪ ګ ڬ ڭ ڮ ػ ؼ`, gaf `گ` and `ڰ ڱ ڲ ڳ ڴ` → `ك` |
| Other letters | `پ ٻ ڀ ٮ` → `ب`; `ٹ ٺ ٿ` → `ت`; `چ ڃ ڄ ڇ` → `ج`; `ڈ`–`ڐ ۮ` → `د`; `ڑ ڕ ۯ` → `ر`; `ژ` → `ز`; `ڤ ڥ ڦ` → `ف`; `ٯ ڨ` → `ق`; `ں ڻ` → `ن`; `ڵ ڶ ڷ ڸ` → `ل`; `ۻ` → `ض` |
| Kept distinct | `ذ` is not folded into `د`, because that fold merges different names (`منذر`, `مندر`) |
| Digits | Arabic-Indic `٠-٩` and Persian `۰-۹` → `0-9` |
| Latin | `A-Z` → `a-z`; Latin-1 Supplement and Latin Extended-A letters → their ASCII base (`é` → `e`, `ł` → `l`, `ñ` → `n`) |
| Deleted | Tatweel; tanwin, harakat, shadda, and sukun; combining madda and hamza (U+0653–U+0655) and other Arabic combining marks; superscript alef; Quranic annotation marks; small waw and small yeh; high hamza; combining Latin diacritics; soft hyphen, zero-width characters, and bidi controls |
| Presentation forms | Every positional form of the letters above (Arabic Presentation Forms-B, and the single-letter forms of Forms-A) folds like its base letter. Multi-letter ligatures other than lam-alef and `ﷲ` are separators |

Teh marbuta and alef maksura fold in the key only. The source column keeps the original spelling.

PostgreSQL and .NET compute the same key whatever Unicode version each ships, and the database locale and collation do not change it. Decomposed input folds like composed input: `ا` + U+0654 gives the same key as `أ`, because the combining hamza and madda are deleted.

### Versions

A rule version is frozen once released. `ArabicSearchVersion.V1` maps to the SQL function `ar_norm_v1`. A rule change adds a new version and a new function (`ar_norm_v2`). The old function stays, because another table may still use it. To move a model to a new version:

1. Change `HasHeadlessArabicSearch` to the new version and scaffold a migration.
2. Replace the scaffolded drop-and-add of each key column with `migrationBuilder.SetArabicSearchKeyExpression(table, column, sourceColumn, version, schema)`, which runs `ALTER TABLE … ALTER COLUMN … SET EXPRESSION AS (…)`.

`SET EXPRESSION` needs PostgreSQL 17 or later. It rewrites every row and rebuilds the table's indexes under an `ACCESS EXCLUSIVE` lock, so plan it like any table rewrite. On PostgreSQL 16 or earlier, drop the key column and add it again with its index. During a rolling deploy, replicas still on the old version query old-version terms against new-version keys and can miss matches until the deploy completes. Nothing errors, because both functions exist.

## Choosing a Provider

PostgreSQL is the only provider. The function needs PostgreSQL 14 or later. The version upgrade path needs PostgreSQL 17.

---

## Headless.Search

The rule table, the C# normalizer, and the provider-agnostic `AddHeadlessSearch` entry point.

### Setup

```bash
dotnet add package Headless.Search
```

The normalizer needs no registration:

```csharp
var key = ArabicSearchNormalizer.Normalize("أَحْمَد  عبد-الله", ArabicSearchVersion.V1); // "احمد عبد الله"
```

`AddHeadlessSearch(setup => …)` requires exactly one `Use…` provider call. It throws `InvalidOperationException` when there is none or more than one, or when it is called twice.

**Verify:** in a unit test, assert that `ArabicSearchNormalizer.Normalize("فاطمه", ArabicSearchVersion.V1)` equals `ArabicSearchNormalizer.Normalize("فاطمة", ArabicSearchVersion.V1)`.

### Design and runtime behavior

- `ArabicSearchNormalizer.Normalize(value, version)` returns `null` for `null`, `""` for text with no alphabet character, and otherwise words separated by single spaces. Normalizing a key again returns it unchanged. It allocates one string per call.
- `ArabicSearchRuleSet.For(version)` returns a version's rows (`OutputAlphabet`, `Mappings`, `Expansions`, `Deletions`), so another provider can render its own function from the same table. An undefined version throws `ArgumentOutOfRangeException`.

---

## Headless.Search.PostgreSql

Creates the `ar_norm_v1` function as a schema-runner step. It references no EF Core package, so SQL and Dapper code can call the function directly. EF Core models add [Headless.Search.PostgreSql.EntityFramework](#headlesssearchpostgresqlentityframework).

### Setup

```bash
dotnet add package Headless.Search.PostgreSql
```

```csharp
builder.Services.AddHeadlessSearch(setup => setup.UsePostgreSql(connectionString));
// or reuse the connection from services.AddPostgreSqlSql(connectionString): setup.UsePostgreSql();
// or bind options: setup.UsePostgreSql(builder.Configuration.GetSection("Search"));
// or: setup.UsePostgreSql(options => { options.ConnectionString = cs; options.Schema = "search"; });
```

[Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features) describes the parameterless overload and the shared `headless` schema.

Deploy the function with `dotnet MyApp.dll resources provision` (or `deploy`). A host start never creates it.

**Verify:** `dotnet MyApp.dll resources verify -n PostgreSql/headless/ArabicSearch` reports `Ok`. Before provisioning, the host start fails, naming the missing resource and `resources provision`.

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that receives the function. |
| `Schema` | `headless` | Validated as a PostgreSQL identifier. An EF Core model passes the same schema to `HasHeadlessArabicSearch`. |

### Design and runtime behavior

- The function is `IMMUTABLE STRICT PARALLEL SAFE`, so PostgreSQL accepts it in a generated column and an index. It ignores the database locale and collation.
- `resources provision` creates the function through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) and records step `ArabicSearch/1` in `headless_schema_history`. A host start only verifies the history and fails fast when the step is missing. The step creates the function only when it is missing, and never replaces an existing one.
- `resources purge` and `resources delete` report this feature as `Unsupported`, because dropping the function would cascade into the application's key columns.
- Create the database with a UTF-8 aware locale: a libc `*.UTF-8` locale such as `en_US.UTF-8` (the official Docker image's default), an ICU locale, or the builtin `C.UTF-8`. `pg_trgm` takes word characters from `LC_CTYPE`. Under libc `LC_CTYPE = 'C'`, no Arabic letter is a word character: `show_trgm` returns nothing, `similarity` and `%` never match, and `LIKE` stays correct but the trigram index cannot narrow it, so every query rechecks every row. Check with `SELECT show_trgm('محمد')`, which must not be empty.
- Without EF Core, query with the function in SQL: `WHERE name_search LIKE '%' || headless.ar_norm_v1(@term) || '%'`, against a column declared `GENERATED ALWAYS AS (headless.ar_norm_v1(name)) STORED` with a GIN `gin_trgm_ops` index. Guard an empty term yourself, as `WhereArabicSearchMatches` does.
- The key columns, their indexes, and the `pg_trgm` extension belong to the application's own migrations. `CREATE EXTENSION pg_trgm` needs a role allowed to create it. Managed PostgreSQL services list `pg_trgm` among their trusted extensions.

---

## Headless.Search.PostgreSql.EntityFramework

EF Core model helpers for the PostgreSQL function: the stored key column and its index, the query function mapping, and the version migration helper.

### Setup

```bash
dotnet add package Headless.Search.PostgreSql.EntityFramework
```

1. Register the function with `AddHeadlessSearch(setup => setup.UsePostgreSql(...))`, as [Headless.Search.PostgreSql](#headlesssearchpostgresql) shows.
2. Configure the model as in [Orientation](#orientation).
3. Scaffold a migration and register it with `AddDbContextMigrations<TContext>()`.

**Verify:** the scaffolded migration adds `CREATE EXTENSION pg_trgm`, the computed key column, and its GIN index. After `resources provision`, a test that inserts `أحمد` finds the row with `WhereArabicSearchMatches(p => p.NameSearch, "احمد")`.

| Member | Effect |
| --- | --- |
| `modelBuilder.HasHeadlessArabicSearch(version, schema = "headless")` | Maps `ArabicSearchFunctions.Key` to `"schema".ar_norm_v{n}`, declares `pg_trgm`, and fixes the version and schema for the model's keys. |
| `entity.HasArabicSearchKey(p => p.Source, p => p.Key, sourceColumn = null)` | A stored generated key column computed from the source column, with a GIN `gin_trgm_ops` index. |
| `entity.HasArabicSearchKey(p => p.Source, "KeyProperty", sourceColumn = null)` | The same, as a shadow `string` property. Query it with `EF.Property<string>(entity, "KeyProperty")`. |
| `query.WhereArabicSearchMatches(p => p.Key, term)` | Keeps the rows whose stored key contains the term's key, computed by the mapped function. A term with an empty key, `null` included, matches no rows. |
| `ArabicSearchFunctions.Key(term)` | Translates to the mapped function in a query. Throws `InvalidOperationException` when called outside one. |
| `migrationBuilder.SetArabicSearchKeyExpression(table, column, sourceColumn, version, schema = null, functionSchema = "headless")` | Rebuilds a key with another version's function through `SET EXPRESSION`. |

`WhereArabicSearchMatches` is in namespace `System.Linq`, holder `HeadlessArabicSearchQueryableExtensions`.

### Design and runtime behavior

- `HasArabicSearchKey` writes `HasComputedColumnSql(..., stored: true)` and an index with `HasMethod("gin")` and `HasOperators("gin_trgm_ops")`.
- `resources provision` applies EF Core migrations (`AddDbContextMigrations<TContext>()`) after every schema step, whatever the registration order, so a migration that adds a key column finds the function. A script-based deploy applies the exported Headless schema script before the EF Core migration script or bundle.
- `WhereArabicSearchMatches` sends the term as a parameter and emits `key LIKE '%' || COALESCE(headless.ar_norm_v1(@term), '') || '%' ESCAPE '' AND length(headless.ar_norm_v1(@term))::int > 0`. The GIN trigram index serves the `LIKE`. The second condition excludes an empty or `null` key.
