---
domain: Search
packages: Search, Search.PostgreSql
---

# Search

> Arabic search keys. A versioned, closed rule table folds hamza seats, alef, teh marbuta, alef maksura, and Persian, Urdu, and Kurdish letter variants; deletes tashkeel, tatweel, and Quranic marks; folds Arabic presentation forms and accented Latin letters; and turns everything else into a word separator. PostgreSQL stores the key in a generated column with a trigram index and normalizes the query term with the same function, so `احمد` finds `أحمد`, `فاطمه` finds `فاطمة`, and `محمدى` finds `محمدي` through an index. The same rules run in C# for highlighting and in-memory work.

## Orientation

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
var people = await db.People
    .Where(p => EF.Functions.Like(p.NameSearch, "%" + ArabicSearchFunctions.Key(term) + "%"))
    .ToListAsync(ct);
```

`HasArabicSearchKey` maps `NameSearch` to `GENERATED ALWAYS AS ("headless".ar_norm_v1("Name")) STORED` with a GIN `gin_trgm_ops` index, and `HasHeadlessArabicSearch` declares the `pg_trgm` extension, so the next scaffolded migration creates all three. `Name` keeps the original text for display. The application never writes `NameSearch`.

## Agent Rules

- Match in the database, never in C#. Query with `ArabicSearchFunctions.Key(term)` so the stored key and the query term come from one function. `ArabicSearchNormalizer` is for highlighting, in-memory filtering, and guards; using it to build the query term would let two implementations disagree silently.
- Use `EF.Functions.Like(key, "%" + ArabicSearchFunctions.Key(term) + "%")`, not `string.Contains`: Npgsql translates `Contains` with a parameter to `strpos`, which the trigram index cannot serve. A normalized term holds only alphabet characters and single spaces, never `%`, `_`, or `\`, so it needs no LIKE escaping.
- Skip the filter for a term that normalizes to `""`: `LIKE '%%'` matches every row. Check with `ArabicSearchNormalizer.Normalize(term, version).Length`; that is a guard, not matching. A term under three characters gives the trigram index nothing to look up, so expect a scan; require a minimum length where the table is large.
- Pass the same version everywhere. `HasHeadlessArabicSearch(version)` fixes it for the whole model, and `ArabicSearchNormalizer.Normalize` takes it with no default, so moving to a new version cannot leave a caller on the old one.
- Call `HasHeadlessArabicSearch` before configuring any entity that calls `HasArabicSearchKey`; the key reads the version and schema from the model and throws `InvalidOperationException` without them.
- `HasArabicSearchKey` reads the source's column name when it is called. Configure `HasColumnName` on the source first, or pass `sourceColumn` when a convention renames the column later.
- The function and its schema must match: pass `HasHeadlessArabicSearch(version, schema)` the `PostgreSqlSearchOptions.Schema` the provider was registered with (default `headless` for both).
- Never `CREATE OR REPLACE` a search function, and never edit a released rule. A stored key keeps the value its function computed at write time; a replaced function makes every stored key disagree with new query terms, with no error. New rules ship as a new version and a new function.

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
| Kept distinct | `ذ` is not folded into `د`: that fold merges different names (`منذر`, `مندر`) |
| Digits | Arabic-Indic `٠-٩` and Persian `۰-۹` → `0-9` |
| Latin | `A-Z` → `a-z`; Latin-1 Supplement and Latin Extended-A letters → their ASCII base (`é` → `e`, `ł` → `l`, `ñ` → `n`) |
| Deleted | Tatweel; tanwin, harakat, shadda, and sukun; combining madda and hamza (U+0653–U+0655) and other Arabic combining marks; superscript alef; Quranic annotation marks; small waw and small yeh; high hamza; combining Latin diacritics; soft hyphen, zero-width characters, and bidi controls |
| Presentation forms | Every positional form of the letters above (Arabic Presentation Forms-B, and the single-letter forms of Forms-A) folds like its base letter. Multi-letter ligatures other than lam-alef and `ﷲ` are separators |

Teh marbuta and alef maksura fold in the key only. The source column keeps the original spelling.

The rules use no Unicode categories, normalization tables, or culture-dependent case mapping, so PostgreSQL and .NET compute the same key whatever Unicode version each ships. Decomposed input folds like composed input (`ا` + U+0654 gives the same key as `أ`), because the combining hamza and madda are deleted.

### Versions

A rule version is frozen once released. `ArabicSearchVersion.V1` maps to the SQL function `ar_norm_v1`. A rule change adds a new version, a new function (`ar_norm_v2`), and a new schema step; the old function stays, because another table may still use it. To move a model:

1. Change `HasHeadlessArabicSearch` to the new version and scaffold a migration.
2. Replace the scaffolded drop-and-add of each key column with `migrationBuilder.SetArabicSearchKeyExpression(table, column, sourceColumn, version, schema)`, which runs `ALTER TABLE … ALTER COLUMN … SET EXPRESSION AS (…)`.

`SET EXPRESSION` needs PostgreSQL 17 or later. It rewrites every row and rebuilds the table's indexes under an `ACCESS EXCLUSIVE` lock; plan it like any table rewrite. On PostgreSQL 16 or earlier, drop the key column and add it again with its index. During a rolling deploy, replicas still on the old version query old-version terms against new-version keys and can miss matches until the deploy completes; nothing errors, because both functions exist.

## Choosing a Provider

PostgreSQL is the only provider. The function needs PostgreSQL 14 or later (a SQL-standard `RETURN` body); the version upgrade path needs 17.

---

## Headless.Search

The rule table, the C# normalizer, and the provider-agnostic setup.

### Setup

```bash
dotnet add package Headless.Search
```

```csharp
var key = ArabicSearchNormalizer.Normalize("أَحْمَد  عبد-الله", ArabicSearchVersion.V1); // "احمد عبد الله"
```

`AddHeadlessSearch(setup => …)` requires exactly one `Use…` provider call and throws `InvalidOperationException` when there are none or several, or when it is called twice.

### Design and runtime behavior

- `ArabicSearchNormalizer.Normalize(value, version)` returns `null` for `null`, `""` for text with no alphabet character, and otherwise words separated by single spaces. Normalizing a key again returns it unchanged. It allocates one string per call and needs no registration.
- `ArabicSearchRuleSet.For(version)` returns a version's rows (`OutputAlphabet`, `Mappings`, `Expansions`, `Deletions`) so another provider can render its own function from the same table. An undefined version throws `ArgumentOutOfRangeException`.

---

## Headless.Search.PostgreSql

The `ar_norm_v1` function as a schema-runner step, the EF Core model helpers, and the version migration helper.

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

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

| Member | Effect |
| --- | --- |
| `modelBuilder.HasHeadlessArabicSearch(version, schema = "headless")` | Maps `ArabicSearchFunctions.Key` to `"schema".ar_norm_v{n}`, declares `pg_trgm`, and fixes the version and schema for the model's keys |
| `entity.HasArabicSearchKey(p => p.Source, p => p.Key, sourceColumn = null)` | A stored generated key column computed from the source column, with a GIN `gin_trgm_ops` index |
| `entity.HasArabicSearchKey(p => p.Source, "KeyProperty", sourceColumn = null)` | The same, as a shadow `string` property; query it with `EF.Property<string>(entity, "KeyProperty")` |
| `ArabicSearchFunctions.Key(term)` | Translates to the mapped function in a query; throws `InvalidOperationException` when called outside one |
| `migrationBuilder.SetArabicSearchKeyExpression(table, column, sourceColumn, version, schema = null, functionSchema = "headless")` | Rebuilds a key with another version's function through `SET EXPRESSION` |

### Configuration

| Option | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | required | The database that receives the function |
| `Schema` | `headless` | Validated as a PostgreSQL identifier. EF Core models pass the same schema to `HasHeadlessArabicSearch` |

### Design and runtime behavior

- The function is `LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE` with a `RETURN` body built from `replace`, `translate`, `regexp_replace`, and `btrim`, all rendered from `ArabicSearchRuleSet` with Unicode-escape literals. The input is cast `COLLATE "C"`, so no database locale or nondeterministic collation affects it.
- `resources provision` creates it through the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) and records `ArabicSearch/1` in `headless_schema_history`; a host start only verifies the history and fails fast when the step is missing. The step creates the function only when `pg_proc` lacks it, and never replaces one.
- EF Core migrations (`AddDbContextMigrations<TContext>()`) run after every schema step in the same `resources provision`, whatever the registration order, so a migration that adds a key column finds the function. A script-based deploy applies the exported Headless schema script before the EF Core migration script or bundle.
- `resources purge` and `resources delete` report this feature as unsupported: dropping the function would cascade into the application's key columns.
- `pg_trgm` takes word characters from the database's `LC_CTYPE`. Create the database with a UTF-8 aware locale: a libc `*.UTF-8` locale such as `en_US.UTF-8` (the official Docker image's default), an ICU locale, or the builtin `C.UTF-8`. Under libc `LC_CTYPE = 'C'` no Arabic letter is a word character: `show_trgm` returns nothing, `similarity` and `%` never match, and `LIKE` stays correct but the trigram index cannot narrow it, so every query rechecks every row. Check with `SELECT show_trgm('محمد')`, which must not be empty.
- The key columns, their indexes, and the `pg_trgm` extension belong to the application's migrations. `CREATE EXTENSION pg_trgm` needs a role allowed to create it; managed PostgreSQL services list `pg_trgm` among their trusted extensions.
