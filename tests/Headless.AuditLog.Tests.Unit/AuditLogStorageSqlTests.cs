// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

public sealed class AuditLogStorageSqlTests
{
    [Fact]
    public void should_keep_the_postgresql_append_only_step_text_that_released_hosts_recorded()
    {
        // given - the schema runner stores a checksum of this text and fails the start of a host whose text differs,
        // so one changed character here breaks every database that already applied the step
        const string released = """
            CREATE OR REPLACE FUNCTION "headless"."audit_log_entries_append_only"() RETURNS trigger LANGUAGE plpgsql AS $headless$
            BEGIN
                IF TG_OP = 'DELETE' AND pg_catalog.current_setting('headless.audit_log_purge', true) = 'on' THEN
                    RETURN OLD;
                END IF;
                RAISE EXCEPTION 'The audit log table %.% is append-only: % is not allowed.', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $headless$;
            DROP TRIGGER IF EXISTS "audit_log_entries_append_only" ON "headless"."audit_log_entries";
            CREATE TRIGGER "audit_log_entries_append_only" BEFORE UPDATE OR DELETE ON "headless"."audit_log_entries" FOR EACH ROW EXECUTE FUNCTION "headless"."audit_log_entries_append_only"();
            DROP TRIGGER IF EXISTS "audit_log_entries_append_only_truncate" ON "headless"."audit_log_entries";
            CREATE TRIGGER "audit_log_entries_append_only_truncate" BEFORE TRUNCATE ON "headless"."audit_log_entries" FOR EACH STATEMENT EXECUTE FUNCTION "headless"."audit_log_entries_append_only"();
            """;

        // when
        var sql = PostgreSqlAuditLogSql.AppendOnly("headless", "audit_log_entries");

        // then
        _Normalize(sql).Should().Be(_Normalize(released));
    }

    [Fact]
    public void should_keep_the_sql_server_append_only_step_text_that_released_hosts_recorded()
    {
        // given
        const string released = """
            EXEC(N'CREATE OR ALTER TRIGGER [headless].[TR_AuditLogEntries_AppendOnly] ON [headless].[AuditLogEntries] AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF NOT EXISTS (SELECT 1 FROM inserted)
                    AND CONVERT(int, SESSION_CONTEXT(N''headless.audit_log_purge'')) = 1
                    RETURN;
                THROW 50000, N''The audit log table headless.AuditLogEntries is append-only: only the retention purge may delete rows.'', 1;
            END');
            """;

        // when
        var sql = SqlServerAuditLogSql.AppendOnly("headless", "AuditLogEntries");

        // then
        _Normalize(sql).Should().Be(_Normalize(released));
        SqlServerAuditLogSql
            .DropAppendOnly("headless", "AuditLogEntries")
            .Should()
            .Be("DROP TRIGGER IF EXISTS [headless].[TR_AuditLogEntries_AppendOnly];");
    }

    [Fact]
    public void should_run_every_owner_function_with_a_pinned_search_path_and_close_it_to_public()
    {
        // when
        var purge = PostgreSqlAuditLogSql.OwnerPurge("compliance", "audit", minimumRetention: null);
        var partitions = PostgreSqlAuditLogSql.PartitionFunctions("compliance", "audit", minimumRetention: null);

        // then - a function that runs with its owner's rights must not resolve names through the caller's path,
        // and PostgreSQL grants EXECUTE on a new function to PUBLIC unless it is revoked
        _Count(purge, "SECURITY DEFINER SET search_path = pg_catalog, pg_temp").Should().Be(1);
        purge.Should().Contain("""REVOKE ALL ON FUNCTION "compliance"."audit_purge" FROM PUBLIC;""");
        _Count(partitions, "SECURITY DEFINER SET search_path = pg_catalog, pg_temp").Should().Be(2);
        partitions.Should().Contain("""REVOKE ALL ON FUNCTION "compliance"."audit_ensure_partitions" FROM PUBLIC;""");
        partitions.Should().Contain("""REVOKE ALL ON FUNCTION "compliance"."audit_drop_partitions" FROM PUBLIC;""");
    }

    [Fact]
    public void should_compile_the_minimum_retention_into_the_purge_and_drop_functions_only_when_configured()
    {
        // when
        var without = PostgreSqlAuditLogSql.OwnerPurge("headless", "audit_log_entries", minimumRetention: null);
        var purge = PostgreSqlAuditLogSql.OwnerPurge("headless", "audit_log_entries", TimeSpan.FromDays(30));
        var partitions = PostgreSqlAuditLogSql.PartitionFunctions(
            "headless",
            "audit_log_entries",
            TimeSpan.FromDays(30)
        );

        // then
        without.Should().NotContain("make_interval(secs");
        purge.Should().Contain("p_cutoff > pg_catalog.now() - pg_catalog.make_interval(secs => 2592000)");
        partitions.Should().Contain("p_cutoff > pg_catalog.now() - pg_catalog.make_interval(secs => 2592000)");
    }

    [Fact]
    public void should_derive_every_object_name_from_the_table_name()
    {
        PostgreSqlAuditLogSql.AppendOnlyTrigger("audit").Should().Be("audit_append_only");
        PostgreSqlAuditLogSql.TruncateTrigger("audit").Should().Be("audit_append_only_truncate");
        PostgreSqlAuditLogSql.PurgeFunction("audit").Should().Be("audit_purge");
        PostgreSqlAuditLogSql.EnsurePartitionsFunction("audit").Should().Be("audit_ensure_partitions");
        PostgreSqlAuditLogSql.DropPartitionsFunction("audit").Should().Be("audit_drop_partitions");
        PostgreSqlAuditLogSql.Qualify("s", "t").Should().Be("\"s\".\"t\"");
        PostgreSqlAuditLogSql.Qualify("s", "we\"ird").Should().Be("\"s\".\"we\"\"ird\"");
        PostgreSqlAuditLogSql
            .EnsurePartitionsNow("s", "t", 6)
            .Should()
            .Be("SELECT \"s\".\"t_ensure_partitions\"(pg_catalog.now(), 6);");
    }

    [Fact]
    public void should_copy_every_storage_option_to_the_registered_instance()
    {
        // given
        var source = new AuditLogStorageOptions
        {
            Schema = "compliance",
            TableName = "audit",
            JsonColumnType = AuditLogJsonColumnType.Json,
            CreatedAtColumnType = "timestamp with time zone",
            AppendOnly = true,
            PurgeAuthority = AuditLogPurgeAuthority.Owner,
            MinimumRetention = TimeSpan.FromDays(90),
            Partitioning = AuditLogPartitioning.Monthly,
            PartitionMonthsAhead = 6,
        };
        var target = new AuditLogStorageOptions();

        // when
        source.CopyTo(target);

        // then
        target.Should().BeEquivalentTo(source);
    }

    private static string _Normalize(string sql) => sql.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int _Count(string text, string value) => text.Split(value).Length - 1;
}
