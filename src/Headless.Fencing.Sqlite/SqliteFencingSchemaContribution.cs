// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.Sqlite;

namespace Headless.Fencing.Sqlite;

/// <summary>
/// The Fencing feature's schema contribution for SQLite: the generation sequence, lease table, its indexes, and the
/// triggers that advance the sequence, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqliteFencingSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(RelationalFencingOptions options, FencingStorageOptions storageOptions)
    {
        var dialect = SqliteDialect.Instance;
        var schema = storageOptions.Schema;
        var t = new FencingTable(dialect, schema);
        var active = FencingTable.StateLiteral(FencingTable.Active);
        var abandoned = FencingTable.StateLiteral(FencingTable.Abandoned);

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // Index and trigger names are global to a SQLite file, so they carry the schema prefix like the tables.
        string global(string pascal) => dialect.Quote(SqliteDialect.QualifiedName(schema, dialect.Name(pascal)));

        // The constraints and indexes are PostgreSQL's (see its contribution for why each holds); TEXT compares with
        // SQLite's BINARY collation, so kinds, resources, and tenant ids match ordinally. SQLite has no sequence
        // objects: the sequence is a one-row table holding the last generation issued, which the dialect's next-value
        // expression reads and these triggers advance whenever a generation is written, inside the same statement and
        // under the database write lock. A purge deletes leases, never the sequence row, so a lease granted again
        // after its row was purged still gets a generation above every earlier one.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {t.Sequence} (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                value INTEGER NOT NULL CHECK (value >= 0)
            );

            INSERT INTO {t.Sequence} (id, value) VALUES (1, 0) ON CONFLICT (id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS {t.Table} (
                {t.TenantId} TEXT NOT NULL,
                {t.Kind} TEXT NOT NULL,
                {t.Resource} TEXT NOT NULL,
                {t.Generation} INTEGER NOT NULL,
                {t.State} INTEGER NOT NULL,
                {t.GrantedAt} TEXT NOT NULL,
                {t.ExpiresAt} TEXT NOT NULL,
                {t.EndedAt} TEXT NULL,
                {t.TakeoverCount} INTEGER NOT NULL DEFAULT 0,
                {t.Progress} BLOB NULL,
                {t.ProgressContract} TEXT NULL,
                CONSTRAINT {name("PK_FencingLeases")} PRIMARY KEY ({t.TenantId}, {t.Kind}, {t.Resource}),
                CONSTRAINT {name("CK_FencingLeases_Generation")} CHECK ({t.Generation} > 0),
                CONSTRAINT {name("CK_FencingLeases_State")} CHECK ({t.State} BETWEEN {active} AND {abandoned}),
                CONSTRAINT {name("CK_FencingLeases_EndedAt")} CHECK (({t.State} = {active}) = ({t.EndedAt} IS NULL)),
                CONSTRAINT {name("CK_FencingLeases_TakeoverCount")} CHECK ({t.TakeoverCount} >= 0),
                CONSTRAINT {name("CK_FencingLeases_Progress")} CHECK (
                    ({t.Progress} IS NULL) = ({t.ProgressContract} IS NULL)
                )
            );

            CREATE INDEX IF NOT EXISTS {global("IX_FencingLeases_ActiveExpiry")}
                ON {t.Table} ({t.Kind}, {t.ExpiresAt}, {t.TenantId}, {t.Resource})
                WHERE {t.State} = {active};

            CREATE INDEX IF NOT EXISTS {global("IX_FencingLeases_Ended")}
                ON {t.Table} ({t.Kind}, {t.EndedAt})
                WHERE {t.State} <> {active};

            CREATE TRIGGER IF NOT EXISTS {global("TR_FencingLeases_GenerationInserted")}
            AFTER INSERT ON {t.Table}
            WHEN NEW.{t.Generation} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.Generation} WHERE id = 1;
            END;

            CREATE TRIGGER IF NOT EXISTS {global("TR_FencingLeases_GenerationUpdated")}
            AFTER UPDATE OF {t.Generation} ON {t.Table}
            WHEN NEW.{t.Generation} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.Generation} WHERE id = 1;
            END;
            """;

        return new SchemaContribution(
            feature: "Fencing",
            dialect: SqliteSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(
                    StepVersion,
                    "Create the generation sequence, lease table, sweep and purge indexes, and sequence triggers.",
                    sql
                ),
            ],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
