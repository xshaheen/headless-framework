// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>Exercises a representative consumer upgrade on connection-local tables, not a full application migration.</summary>
public abstract class JobsKeyedMigrationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    protected abstract bool IsPostgreSql { get; }

    private string _Table => IsPostgreSql ? "\"keyed_migration\"" : "\"#keyed_migration\"";

    public virtual async Task upgrade_keeps_legacy_rows_wholly_unkeyed_and_preserves_payload_bytes()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        byte[] payload = [0, 255, 32, 13, 10, 123, 125];
        await _ExecuteAsync(
            connection,
            $"INSERT INTO {_Table} (\"Id\", \"Function\", \"Request\") VALUES (1, 'deadline', @payload);",
            parameters: [("@payload", payload)]
        );
        await _UpgradeAsync(connection);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT \"Request\", \"BusinessKey\", \"IntentFingerprint\", \"FingerprintAlgorithm\", \"Generation\", \"IsCurrentGeneration\" FROM {_Table};";
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        (await reader.ReadAsync(AbortToken)).Should().BeTrue();
        (await reader.GetFieldValueAsync<byte[]>(0, AbortToken)).Should().Equal(payload);
        for (var column = 1; column <= 5; column++)
        {
            (await reader.IsDBNullAsync(column, AbortToken))
                .Should()
                .BeTrue("migration must not invent business intent for legacy rows");
        }
    }

    public virtual async Task keyed_constraints_reject_partial_metadata_and_chain_membership()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        await _UpgradeAsync(connection);
        await _InsertKeyedAsync(connection, 1, null, "invoice", 1, current: true);

        foreach (
            var column in new[]
            {
                "BusinessKey",
                "IntentFingerprint",
                "FingerprintAlgorithm",
                "Generation",
                "IsCurrentGeneration",
            }
        )
        {
            var partial = async () =>
                await _ExecuteAsync(connection, $"UPDATE {_Table} SET \"{column}\" = NULL WHERE \"Id\" = 1;");
            await partial.Should().ThrowAsync<DbException>();
        }

        foreach (
            var assignment in new[]
            {
                "\"Generation\" = 0",
                "\"Generation\" = -1",
                "\"ParentId\" = 99",
                "\"RunCondition\" = 'OnSuccess'",
                "\"BusinessKey\" = ''",
                "\"IntentFingerprint\" = ''",
                "\"FingerprintAlgorithm\" = ''",
            }
        )
        {
            var invalid = async () =>
                await _ExecuteAsync(connection, $"UPDATE {_Table} SET {assignment} WHERE \"Id\" = 1;");
            await invalid.Should().ThrowAsync<DbException>();
        }

        (await _CountAsync(connection, "\"Generation\" = 1 AND \"ParentId\" IS NULL AND \"RunCondition\" IS NULL"))
            .Should()
            .Be(1);
    }

    public virtual async Task uniqueness_covers_system_tenants_current_and_historical_generations()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        await _UpgradeAsync(connection);

        foreach (var (tenant, firstId) in new[] { ((string?)null, 10), ("tenant-a", 20), ("tenant-b", 30) })
        {
            await _InsertKeyedAsync(connection, firstId, tenant, "invoice", 1, current: false);
            await _InsertKeyedAsync(connection, firstId + 1, tenant, "invoice", 2, current: true);
            var duplicateHistory = async () =>
                await _InsertKeyedAsync(connection, firstId + 2, tenant, "invoice", 1, current: false);
            await duplicateHistory.Should().ThrowAsync<DbException>();
            var secondCurrent = async () =>
                await _InsertKeyedAsync(connection, firstId + 3, tenant, "invoice", 3, current: true);
            await secondCurrent.Should().ThrowAsync<DbException>();
            await _InsertKeyedAsync(connection, firstId + 4, tenant, "Invoice", 1, current: true);
        }

        (await _CountAsync(connection, "1 = 1")).Should().Be(9);
    }

    public virtual async Task downgrade_preflight_rejects_retained_history_even_without_a_current_row()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        await _ExecuteAsync(connection, $"INSERT INTO {_Table} (\"Id\", \"Function\") VALUES (1, 'legacy');");
        await _UpgradeAsync(connection);
        await _PreflightDowngradeAsync(connection);
        await _InsertKeyedAsync(connection, 2, null, "retained", 1, current: false);

        var downgrade = async () => await _PreflightDowngradeAsync(connection);
        await downgrade.Should().ThrowAsync<InvalidOperationException>().WithMessage("*roll forward*");
        (await _CountAsync(connection, "\"BusinessKey\" = 'retained'")).Should().Be(1);
    }

    private Task _CreateLegacyAsync(DbConnection connection)
    {
        var create = IsPostgreSql ? "CREATE TEMPORARY TABLE" : "CREATE TABLE";
        var text = IsPostgreSql ? "varchar(200) COLLATE \"C\"" : "nvarchar(200) COLLATE Latin1_General_100_BIN2";
        var bytes = IsPostgreSql ? "bytea" : "varbinary(max)";
        return _ExecuteAsync(
            connection,
            $"{create} {_Table} (\"Id\" int PRIMARY KEY, \"Function\" {text} NOT NULL, \"TenantId\" {text} NULL, \"Request\" {bytes} NULL, \"ParentId\" int NULL, \"RunCondition\" varchar(32) NULL);"
        );
    }

    private async Task _UpgradeAsync(DbConnection connection)
    {
        await using var transaction = await connection.BeginTransactionAsync(AbortToken);
        var text = IsPostgreSql ? "varchar" : "nvarchar";
        var collation = IsPostgreSql ? "\"C\"" : "Latin1_General_100_BIN2";
        var boolean = IsPostgreSql ? "boolean" : "bit";
        var truth = IsPostgreSql ? "TRUE" : "1";
        var columns = new[]
        {
            $"\"BusinessKey\" {text}(200) COLLATE {collation} NULL",
            $"\"IntentFingerprint\" {text}(64) NULL",
            $"\"FingerprintAlgorithm\" {text}(16) NULL",
            "\"Generation\" bigint NULL",
            $"\"IsCurrentGeneration\" {boolean} NULL",
        };
        foreach (var column in columns)
        {
            await _ExecuteAsync(connection, $"ALTER TABLE {_Table} ADD {column};", transaction: transaction);
        }

        await _ExecuteAsync(
            connection,
            $"""
            ALTER TABLE {_Table} ADD CHECK (
                ("BusinessKey" IS NULL AND "IntentFingerprint" IS NULL AND "FingerprintAlgorithm" IS NULL AND "Generation" IS NULL AND "IsCurrentGeneration" IS NULL)
                OR ("BusinessKey" IS NOT NULL AND "IntentFingerprint" IS NOT NULL AND "FingerprintAlgorithm" IS NOT NULL AND "Generation" IS NOT NULL AND "IsCurrentGeneration" IS NOT NULL
                    AND "BusinessKey" <> '' AND "IntentFingerprint" <> '' AND "FingerprintAlgorithm" <> ''
                    AND "Generation" > 0 AND "ParentId" IS NULL AND "RunCondition" IS NULL));
            CREATE UNIQUE INDEX "keyed_system_generation" ON {_Table} ("Function", "BusinessKey", "Generation") WHERE "BusinessKey" IS NOT NULL AND "TenantId" IS NULL;
            CREATE UNIQUE INDEX "keyed_tenant_generation" ON {_Table} ("TenantId", "Function", "BusinessKey", "Generation") WHERE "BusinessKey" IS NOT NULL AND "TenantId" IS NOT NULL;
            CREATE UNIQUE INDEX "keyed_system_current" ON {_Table} ("Function", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "TenantId" IS NULL AND "IsCurrentGeneration" = {truth};
            CREATE UNIQUE INDEX "keyed_tenant_current" ON {_Table} ("TenantId", "Function", "BusinessKey") WHERE "BusinessKey" IS NOT NULL AND "TenantId" IS NOT NULL AND "IsCurrentGeneration" = {truth};
            """,
            transaction: transaction
        );
        await transaction.CommitAsync(AbortToken);
    }

    private Task _InsertKeyedAsync(
        DbConnection connection,
        int id,
        string? tenant,
        string key,
        long generation,
        bool current
    ) =>
        _ExecuteAsync(
            connection,
            $"""
            INSERT INTO {_Table} ("Id", "TenantId", "Function", "BusinessKey", "IntentFingerprint", "FingerprintAlgorithm", "Generation", "IsCurrentGeneration")
            VALUES (@id, @tenant, 'deadline', @key, @fingerprint, 'v1', @generation, @current);
            """,
            parameters:
            [
                ("@id", id),
                ("@tenant", tenant),
                ("@key", key),
                ("@fingerprint", new string('a', 64)),
                ("@generation", generation),
                ("@current", current),
            ]
        );

    private async Task _PreflightDowngradeAsync(DbConnection connection)
    {
        if (
            await _CountAsync(
                connection,
                "\"BusinessKey\" IS NOT NULL OR \"IntentFingerprint\" IS NOT NULL OR \"FingerprintAlgorithm\" IS NOT NULL OR \"Generation\" IS NOT NULL OR \"IsCurrentGeneration\" IS NOT NULL"
            ) != 0
        )
        {
            throw new InvalidOperationException("Retained keyed history is incompatible with downgrade; roll forward.");
        }
    }

    private async Task<int> _CountAsync(DbConnection connection, string predicate)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {_Table} WHERE {predicate};";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(AbortToken),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private async Task _ExecuteAsync(
        DbConnection connection,
        string sql,
        (string Name, object? Value)[]? parameters = null,
        DbTransaction? transaction = null
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters ?? [])
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync(AbortToken);
    }
}
