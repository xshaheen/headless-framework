// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text;
using Headless.Sql.PostgreSql;

namespace Headless.Search.PostgreSql;

/// <summary>Renders the PostgreSQL function of one Arabic search rule version from its <see cref="ArabicSearchRuleSet" />.</summary>
internal static class PostgreSqlArabicSearchFunction
{
    /// <summary>The schema-qualified function name: <c>"headless".ar_norm_v1</c>.</summary>
    public static string QualifiedName(string schema, ArabicSearchVersion version)
    {
        return $"{PostgreSqlDialect.Instance.Quote(schema)}.{Name(version)}";
    }

    /// <summary>The unqualified function name of <paramref name="version" />: <c>ar_norm_v1</c>.</summary>
    public static string Name(ArabicSearchVersion version)
    {
        return string.Create(CultureInfo.InvariantCulture, $"ar_norm_v{(int)ArabicSearchRuleSet.For(version).Version}");
    }

    /// <summary>
    /// The call that builds a stored key from <paramref name="column" />: <c>"headless".ar_norm_v1("name")</c>. The
    /// generated column and a later <c>SET EXPRESSION</c> must render it identically, or EF Core sees model drift.
    /// </summary>
    public static string Call(string schema, ArabicSearchVersion version, string column)
    {
        return $"{QualifiedName(schema, version)}({PostgreSqlDialect.Instance.Quote(column)})";
    }

    /// <summary>
    /// The idempotent DDL that creates the function when the schema lacks it. It never replaces an existing function:
    /// stored generated columns and indexes depend on it, and a replaced body would leave their values stale with no
    /// error. A rule change is a new version, so it is a new function.
    /// </summary>
    public static string CreateSql(string schema, ArabicSearchVersion version)
    {
        var rules = ArabicSearchRuleSet.For(version);
        var name = Name(version);

        // translate() deletes each source character that has no counterpart in the target string, so the deletions
        // follow the mappings in the source string. Rows are sorted so the rendered SQL, and so the step checksum,
        // depends only on the rules.
        var mappings = rules.Mappings.OrderBy(static m => m.Key).ToList();
        var from = string.Concat(mappings.Select(static m => m.Key)) + string.Concat(rules.Deletions);
        var to = string.Concat(mappings.Select(static m => m.Value));

        // Collation "C": PostgreSQL refuses regular expressions over a nondeterministic collation, and no step here
        // may depend on a locale.
        var expanded = "(value COLLATE \"C\")";

        foreach (var (source, result) in rules.Expansions.OrderBy(static e => e.Key))
        {
            expanded = $"replace({expanded}, {_Literal(source.ToString())}, {_Literal(result)})";
        }

        var separators = _Literal("[^" + string.Concat(rules.OutputAlphabet) + "]+");

        return $"""
            DO $headless_{name}$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                    WHERE n.nspname = '{schema.Replace("'", "''", StringComparison.Ordinal)}' AND p.proname = '{name}'
                ) THEN
                    CREATE FUNCTION {QualifiedName(schema, version)}(value text) RETURNS text
                        LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE
                        RETURN btrim(
                            regexp_replace(
                                translate(
                                    {expanded},
                                    {_Literal(from)},
                                    {_Literal(to)}
                                ),
                                {separators},
                                ' ',
                                'g'
                            ),
                            ' '
                        );
                END IF;
            END
            $headless_{name}$;
            """;
    }

    // A Unicode-escape literal keeps the SQL ASCII, so invisible characters (zero-width marks, bidi controls) stay
    // reviewable and no character can end the literal or start an escape. Letters and digits stay readable.
    private static string _Literal(string value)
    {
        var builder = new StringBuilder("U&'", (value.Length * 5) + 4);

        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '[' or ']' or '^' or '+')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(CultureInfo.InvariantCulture, $"\\{(int)c:X4}");
            }
        }

        return builder.Append('\'').ToString();
    }
}
