// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.AuditLog.PostgreSql;

/// <summary>
/// The PostgreSQL DDL behind an append-only or partitioned audit log table. This provider's schema steps and the
/// PostgreSQL Entity Framework migration helpers both emit it, so a table created by either carries the same objects.
/// </summary>
/// <remarks>
/// Every name is spliced into the SQL, so callers pass only identifiers that already passed the storage identifier
/// rules. Every function that runs with its owner's rights pins <c>search_path</c>, so a caller cannot make it resolve
/// an object the caller planted.
/// </remarks>
internal static class PostgreSqlAuditLogSql
{
    /// <summary>The most months ahead a caller may ask the partition function to create.</summary>
    public const int MaxMonthsAhead = AuditLogStorageOptions.MaxPartitionMonthsAhead;

    // Every suffix below is shorter than the longest index suffix, so the option validators' derived-name check
    // (FitsDerivedPostgreSqlNames over the index names) already keeps these names inside PostgreSQL's 63 bytes.
    private const string _AppendOnly = "append_only";

    // The functions run as their owner for a caller that may hold no DDL rights, so they pin the search path. UTC
    // makes a month start at the same instant whatever the caller's session time zone is.
    private const string _OwnerFunctionSettings =
        "LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET timezone = 'UTC'";

    // Creating or dropping a partition takes an ACCESS EXCLUSIVE lock on the parent. A short lock timeout fails the
    // call instead of queueing every writer behind it while a long transaction holds the table.
    private const string _PartitionLockTimeout = "SET lock_timeout = '5s'";

    /// <summary>The append-only trigger on the table, which is also the name of its function.</summary>
    public static string AppendOnlyTrigger(string table) => $"{table}_{_AppendOnly}";

    /// <summary>The statement trigger that refuses <c>TRUNCATE</c>, on the table and on each partition.</summary>
    public static string TruncateTrigger(string table) => $"{table}_{_AppendOnly}_truncate";

    /// <summary>The unqualified name of the owner's purge function.</summary>
    public static string PurgeFunction(string table) => $"{table}_purge";

    /// <summary>The unqualified name of the function that creates missing partitions.</summary>
    public static string EnsurePartitionsFunction(string table) => $"{table}_ensure_partitions";

    /// <summary>The unqualified name of the function that drops expired partitions.</summary>
    public static string DropPartitionsFunction(string table) => $"{table}_drop_partitions";

    /// <summary>
    /// The unqualified name of the plain table a migration creates first when it creates the audit log partitioned;
    /// see <see cref="PartitionedFromTemplate"/>.
    /// </summary>
    public static string TemplateTable(string table) => $"{table}_template";

    /// <summary>Returns <paramref name="name"/> quoted and qualified by <paramref name="schema"/>.</summary>
    public static string Qualify(string schema, string name) => $"{_Quote(schema)}.{_Quote(name)}";

    /// <summary>
    /// Creates the audit log table partitioned by month from the plain table <see cref="TemplateTable"/> names, then
    /// drops that table. For a migration: EF Core has no partitioning API, so the migration creates the columns it
    /// scaffolded under the template's name and PostgreSQL copies their definitions.
    /// </summary>
    /// <remarks>
    /// <c>LIKE</c> copies the columns with their defaults, identity, checks, and comments. It names a copied key or
    /// index by its own rules, so the caller adds the keys and indexes to the new table under the model's names. Two
    /// things are carried over here: the table's own comment, which is not a column property, and the names of the
    /// not-null constraints, which PostgreSQL 18 derives from the table name and copies as the template's.
    /// </remarks>
    public static string PartitionedFromTemplate(string schema, string table)
    {
        var qualified = Qualify(schema, table);
        var templateName = TemplateTable(table);
        var template = Qualify(schema, templateName);
        var suffixStart = (templateName.Length + 1).ToString(CultureInfo.InvariantCulture);

        // A name PostgreSQL shortened to fit 63 bytes no longer starts with the template's name and keeps it; only
        // the name is affected.
        return $"""
            CREATE TABLE {qualified} (LIKE {template} INCLUDING ALL EXCLUDING INDEXES) PARTITION BY RANGE ("created_at");
            DO $headless$
            DECLARE
                v_comment text := pg_catalog.obj_description('{template}'::regclass, 'pg_class');
                v_constraint text;
            BEGIN
                IF v_comment IS NOT NULL THEN
                    EXECUTE pg_catalog.format('COMMENT ON TABLE %s IS %L', '{qualified}'::regclass, v_comment);
                END IF;
                FOR v_constraint IN
                    SELECT c.conname::text FROM pg_catalog.pg_constraint AS c
                    WHERE c.conrelid = '{qualified}'::regclass
                        AND c.contype = 'n'
                        AND pg_catalog.starts_with(c.conname::text, '{templateName}_')
                LOOP
                    EXECUTE pg_catalog.format(
                        'ALTER TABLE %s RENAME CONSTRAINT %I TO %I',
                        '{qualified}'::regclass, v_constraint, '{table}' || pg_catalog.substr(v_constraint, {suffixStart})
                    );
                END LOOP;
            END;
            $headless$;
            DROP TABLE {template};
            """;
    }

    /// <summary>
    /// The append-only guard: a row trigger that refuses <c>UPDATE</c> and <c>DELETE</c> and a statement trigger that
    /// refuses <c>TRUNCATE</c>, over one function that lets a <c>DELETE</c> through while the purge setting is on.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL provider records this text as a schema step, and the runner compares its checksum on every start.
    /// Changing one character fails the start of every host that already applied it.
    /// </remarks>
    public static string AppendOnly(string schema, string table)
    {
        var name = AppendOnlyTrigger(table);
        var function = Qualify(schema, name);
        var qualified = Qualify(schema, table);

        // Row triggers cover UPDATE and DELETE; TRUNCATE skips them, so a statement trigger refuses it as well. Only a
        // DELETE run with the purge setting on passes. The setting lives in the session, so this guards application
        // code, not a principal that can run arbitrary SQL.
        return $"""
            CREATE OR REPLACE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $headless$
            BEGIN
                IF TG_OP = 'DELETE' AND pg_catalog.current_setting('{AuditLogStorageNames.PurgeSetting}', true) = 'on' THEN
                    RETURN OLD;
                END IF;
                RAISE EXCEPTION 'The audit log table %.% is append-only: % is not allowed.', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $headless$;
            DROP TRIGGER IF EXISTS "{name}" ON {qualified};
            CREATE TRIGGER "{name}" BEFORE UPDATE OR DELETE ON {qualified} FOR EACH ROW EXECUTE FUNCTION {function}();
            DROP TRIGGER IF EXISTS "{name}_truncate" ON {qualified};
            CREATE TRIGGER "{name}_truncate" BEFORE TRUNCATE ON {qualified} FOR EACH STATEMENT EXECUTE FUNCTION {function}();
            """;
    }

    /// <summary>
    /// Moves the purge authority from the session setting to the table's owner: replaces the append-only function with
    /// one that also requires the deleting role to hold the owner's rights, and adds the purge function that runs with
    /// them. Run it after <see cref="AppendOnly"/>.
    /// </summary>
    /// <param name="schema">The table's schema.</param>
    /// <param name="table">The unquoted table name.</param>
    /// <param name="minimumRetention">
    /// The retention the purge function enforces whatever cutoff its caller passes, or <see langword="null"/> for none.
    /// </param>
    public static string OwnerPurge(string schema, string table, TimeSpan? minimumRetention)
    {
        var function = Qualify(schema, AppendOnlyTrigger(table));
        var purge = Qualify(schema, PurgeFunction(table));
        var qualified = Qualify(schema, table);

        // The trigger function runs as the role that issued the statement. Inside the purge function that role is the
        // function's owner, so the setting alone no longer passes: a login that sets it and deletes directly is not
        // the owner. A role that holds the owner's rights could drop the trigger anyway, so it is let through too.
        return $"""
            CREATE OR REPLACE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $headless$
            BEGIN
                IF TG_OP = 'DELETE'
                    AND pg_catalog.current_setting('{AuditLogStorageNames.PurgeSetting}', true) = 'on'
                    AND pg_catalog.pg_has_role((SELECT c.relowner FROM pg_catalog.pg_class AS c WHERE c.oid = TG_RELID), 'USAGE')
                THEN
                    RETURN OLD;
                END IF;
                RAISE EXCEPTION 'The audit log table %.% is append-only: % is not allowed.', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $headless$;
            CREATE OR REPLACE FUNCTION {purge}(p_cutoff {qualified}."created_at"%TYPE, p_limit integer) RETURNS integer
                {_OwnerFunctionSettings} AS $headless$
            DECLARE
                v_deleted integer;
            BEGIN
            {_MinimumRetentionCheck(minimumRetention)}
                PERFORM pg_catalog.set_config('{AuditLogStorageNames.PurgeSetting}', 'on', true);
                WITH expired AS (
                    SELECT "created_at", "id" FROM {qualified}
                    WHERE "created_at" < p_cutoff
                    ORDER BY "created_at", "id"
                    LIMIT p_limit
                ), deleted AS (
                    DELETE FROM {qualified} AS t
                    USING expired AS e
                    WHERE t."created_at" = e."created_at" AND t."id" = e."id"
                    RETURNING 1
                )
                SELECT pg_catalog.count(*)::integer INTO v_deleted FROM deleted;
                PERFORM pg_catalog.set_config('{AuditLogStorageNames.PurgeSetting}', 'off', true);
                RETURN v_deleted;
            END;
            $headless$;
            REVOKE ALL ON FUNCTION {purge} FROM PUBLIC;
            """;
    }

    /// <summary>
    /// The partition functions: one creates the missing monthly partitions and guards each against <c>TRUNCATE</c>,
    /// the other drops the partitions past a cutoff. Both run with their owner's rights and are closed to
    /// <c>PUBLIC</c>.
    /// </summary>
    /// <param name="schema">The table's schema.</param>
    /// <param name="table">The unquoted table name.</param>
    /// <param name="minimumRetention">
    /// The retention the drop function enforces whatever cutoff its caller passes, or <see langword="null"/> for none.
    /// </param>
    public static string PartitionFunctions(string schema, string table, TimeSpan? minimumRetention)
    {
        var qualified = Qualify(schema, table);
        var ensure = Qualify(schema, EnsurePartitionsFunction(table));
        var drop = Qualify(schema, DropPartitionsFunction(table));
        var lockKey = $"{schema}.{table}";
        var maxMonths = MaxMonthsAhead.ToString(CultureInfo.InvariantCulture);

        // A statement trigger on a partitioned table is not cloned to its partitions, so a TRUNCATE aimed at one
        // partition would skip the parent's guard. The ensure function therefore gives every partition its own, new or
        // old, whenever the parent has one: turning append-only on later is repaired by the next call.
        return $"""
            CREATE OR REPLACE FUNCTION {ensure}(p_from timestamp with time zone, p_months_ahead integer) RETURNS integer
                {_OwnerFunctionSettings} {_PartitionLockTimeout} AS $headless$
            DECLARE
                v_parent regclass := '{qualified}'::regclass;
                v_first date;
                v_start date;
                v_name text;
                v_partition regclass;
                v_created integer := 0;
            BEGIN
                IF p_from IS NULL OR p_months_ahead IS NULL OR p_months_ahead < 0 OR p_months_ahead > {maxMonths} THEN
                    RAISE EXCEPTION 'The audit log partitions need a start instant and 0 to {maxMonths} months ahead.'
                        USING ERRCODE = 'invalid_parameter_value';
                END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('{lockKey}', 0));
                v_first := pg_catalog.date_trunc('month', p_from)::date;
                FOR v_offset IN 0..p_months_ahead LOOP
                    v_start := (v_first + pg_catalog.make_interval(months => v_offset))::date;
                    v_name := '{table}_' || pg_catalog.to_char(v_start, 'YYYY_MM');
                    CONTINUE WHEN pg_catalog.to_regclass(pg_catalog.format('%I.%I', '{schema}', v_name)) IS NOT NULL;
                    EXECUTE pg_catalog.format(
                        'CREATE TABLE %I.%I PARTITION OF %s FOR VALUES FROM (%L) TO (%L)',
                        '{schema}', v_name, v_parent, v_start, (v_start + pg_catalog.make_interval(months => 1))::date
                    );
                    v_created := v_created + 1;
                END LOOP;
            {_TruncateGuardLoop(schema, table)}
                RETURN v_created;
            END;
            $headless$;
            REVOKE ALL ON FUNCTION {ensure} FROM PUBLIC;
            CREATE OR REPLACE FUNCTION {drop}(p_cutoff timestamp with time zone) RETURNS integer
                {_OwnerFunctionSettings} {_PartitionLockTimeout} AS $headless$
            DECLARE
                v_partition record;
                v_dropped integer := 0;
            BEGIN
                IF p_cutoff IS NULL THEN
                    RAISE EXCEPTION 'The audit log partitions need a cutoff.' USING ERRCODE = 'invalid_parameter_value';
                END IF;
            {_MinimumRetentionCheck(minimumRetention)}
                PERFORM pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended('{lockKey}', 0));
                FOR v_partition IN
                    SELECT
                        c.oid::regclass AS name,
                        pg_catalog.substring(pg_catalog.pg_get_expr(c.relpartbound, c.oid), 'TO \(''([^'']+)''\)$') AS upper_bound
                    FROM pg_catalog.pg_inherits AS i
                    JOIN pg_catalog.pg_class AS c ON c.oid = i.inhrelid
                    WHERE i.inhparent = '{qualified}'::regclass
                LOOP
                    -- A default partition, or one open to MAXVALUE, has no upper bound to compare and is never dropped.
                    CONTINUE WHEN v_partition.upper_bound IS NULL;
                    IF v_partition.upper_bound::timestamp with time zone <= p_cutoff THEN
                        EXECUTE pg_catalog.format('DROP TABLE %s', v_partition.name);
                        v_dropped := v_dropped + 1;
                    END IF;
                END LOOP;
                RETURN v_dropped;
            END;
            $headless$;
            REVOKE ALL ON FUNCTION {drop} FROM PUBLIC;
            """;
    }

    /// <summary>
    /// Stops with an error unless the audit log table is a partitioned table. The partition functions only add and
    /// drop partitions; PostgreSQL cannot partition a table in place, so the migration that creates the table must
    /// create it partitioned.
    /// </summary>
    public static string RequirePartitioned(string schema, string table)
    {
        var qualified = Qualify(schema, table);

        return $"""
            DO $headless$
            BEGIN
                IF (SELECT c.relkind FROM pg_catalog.pg_class AS c WHERE c.oid = '{qualified}'::regclass) <> 'p' THEN
                    RAISE EXCEPTION 'The audit log table {schema}.{table} is not partitioned, and PostgreSQL cannot partition a table in place. Add the audit log partitions in the migration that creates the table.'
                        USING ERRCODE = 'object_not_in_prerequisite_state';
                END IF;
            END;
            $headless$;
            """;
    }

    /// <summary>
    /// Gives every existing partition the <c>TRUNCATE</c> guard its parent has. For a table that was partitioned
    /// before it became append-only; later partitions get theirs from the ensure function.
    /// </summary>
    public static string GuardPartitionsAgainstTruncate(string schema, string table)
    {
        return $"""
            DO $headless$
            DECLARE
                v_parent regclass := '{Qualify(schema, table)}'::regclass;
                v_partition regclass;
            BEGIN
            {_TruncateGuardLoop(schema, table)}
            END;
            $headless$;
            """;
    }

    /// <summary>The statement that creates the partitions from now through <paramref name="monthsAhead"/> months ahead.</summary>
    /// <remarks>
    /// A block, not a bare <c>SELECT</c>: an idempotent migration script runs each migration's statements inside a
    /// PL/pgSQL block, where a <c>SELECT</c> with no target fails.
    /// </remarks>
    public static string EnsurePartitionsNow(string schema, string table, int monthsAhead)
    {
        var months = monthsAhead.ToString(CultureInfo.InvariantCulture);

        return $"""
            DO $headless$
            BEGIN
                PERFORM {Qualify(schema, EnsurePartitionsFunction(table))}(pg_catalog.now(), {months});
            END;
            $headless$;
            """;
    }

    /// <summary>
    /// Removes the functions <see cref="PartitionFunctions"/> created. The partitions belong to the table and go when
    /// it is dropped.
    /// </summary>
    public static string DropPartitionFunctions(string schema, string table)
    {
        return $"""
            DROP FUNCTION IF EXISTS {Qualify(schema, EnsurePartitionsFunction(table))};
            DROP FUNCTION IF EXISTS {Qualify(schema, DropPartitionsFunction(table))};
            """;
    }

    /// <summary>
    /// Lets <paramref name="role"/> call the functions <see cref="PartitionFunctions"/> created, which are closed to
    /// <c>PUBLIC</c>.
    /// </summary>
    public static string GrantPartitionFunctions(string schema, string table, string role)
    {
        return $"GRANT EXECUTE ON FUNCTION {Qualify(schema, EnsurePartitionsFunction(table))}, {Qualify(schema, DropPartitionsFunction(table))} TO {_Quote(role)};";
    }

    /// <summary>Removes everything <see cref="AppendOnly"/> and <see cref="OwnerPurge"/> created.</summary>
    public static string DropAppendOnly(string schema, string table)
    {
        var name = AppendOnlyTrigger(table);
        var qualified = Qualify(schema, table);

        // Dropping the function with CASCADE takes every trigger that calls it, the partitions' TRUNCATE guards included.
        return $"""
            DROP TRIGGER IF EXISTS "{name}" ON {qualified};
            DROP TRIGGER IF EXISTS "{name}_truncate" ON {qualified};
            DROP FUNCTION IF EXISTS {Qualify(schema, PurgeFunction(table))};
            DROP FUNCTION IF EXISTS {Qualify(schema, name)}() CASCADE;
            """;
    }

    // Expects v_parent and v_partition, both regclass, declared by the block it is spliced into.
    private static string _TruncateGuardLoop(string schema, string table)
    {
        var appendOnlyFunction = Qualify(schema, AppendOnlyTrigger(table));
        var truncateTrigger = TruncateTrigger(table);

        return $"""
                IF EXISTS (SELECT 1 FROM pg_catalog.pg_trigger AS t WHERE t.tgrelid = v_parent AND t.tgname = '{truncateTrigger}') THEN
                    FOR v_partition IN
                        SELECT i.inhrelid::regclass FROM pg_catalog.pg_inherits AS i
                        WHERE i.inhparent = v_parent
                            AND NOT EXISTS (
                                SELECT 1 FROM pg_catalog.pg_trigger AS t
                                WHERE t.tgrelid = i.inhrelid AND t.tgname = '{truncateTrigger}'
                            )
                    LOOP
                        EXECUTE pg_catalog.format(
                            'CREATE TRIGGER %I BEFORE TRUNCATE ON %s FOR EACH STATEMENT EXECUTE FUNCTION %s()',
                            '{truncateTrigger}', v_partition, '{appendOnlyFunction}'
                        );
                    END LOOP;
                END IF;
            """;
    }

    private static string _Quote(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string _MinimumRetentionCheck(TimeSpan? minimumRetention)
    {
        if (minimumRetention is not { } retention)
        {
            return "    -- No minimum retention is configured, so the caller's cutoff stands.";
        }

        var seconds = ((long)retention.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        return $"""
                IF p_cutoff > pg_catalog.now() - pg_catalog.make_interval(secs => {seconds}) THEN
                    RAISE EXCEPTION 'The audit log keeps entries for at least {seconds} seconds; the cutoff % is inside that period.', p_cutoff
                        USING ERRCODE = 'insufficient_privilege';
                END IF;
            """;
    }
}
