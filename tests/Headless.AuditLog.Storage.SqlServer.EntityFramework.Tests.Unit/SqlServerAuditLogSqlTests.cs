// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog.SqlServer;

namespace Tests;

public sealed class SqlServerAuditLogSqlTests
{
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

    private static string _Normalize(string sql) => sql.Replace("\r\n", "\n", StringComparison.Ordinal);
}
