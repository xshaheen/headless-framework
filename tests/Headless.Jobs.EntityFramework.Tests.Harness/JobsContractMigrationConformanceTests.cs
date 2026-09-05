// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Testing.Tests;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Representative consumer-owned migration of the affected legacy columns on real providers. Connection-local
/// temporary tables omit unrelated scheduling columns; this is not a framework schema initializer or an application migration.
/// </summary>
public abstract class JobsContractMigrationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    protected abstract bool IsPostgreSql { get; }

    private string _Table(string name) => IsPostgreSql ? $"\"migration_{name}\"" : $"\"#migration_{name}\"";

    public virtual async Task legacy_upgrade_preserves_available_payload_bytes_and_ordinal_contract_identity()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        byte[] payload = [0, 255, 32, 13, 10, 123, 125];
        await _SeedAsync(connection, "Invoice.Send", payload);
        await _UpgradeAsync(connection);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"SELECT \"Function\", \"ContractVersion\", \"Request\", \"TenantId\", \"CorrelationId\", \"CausationId\" FROM {_Table("CronJobOccurrences")};";
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            (await reader.ReadAsync(AbortToken)).Should().BeTrue();
            reader.GetString(0).Should().Be("Invoice.Send");
            reader.GetString(1).Should().Be("1");
            (await reader.GetFieldValueAsync<byte[]>(2, AbortToken)).Should().Equal(payload);
            for (var column = 3; column < 6; column++)
            {
                (await reader.IsDBNullAsync(column, AbortToken)).Should().BeTrue();
            }
        }

        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            (
                await _ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"Function\" = @name AND \"ContractVersion\" = @version;",
                    ("@name", "Invoice.Send"),
                    ("@version", "1")
                )
            )
                .Should()
                .Be(1);
            (
                await _ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"Function\" = @name;",
                    ("@name", "invoice.send")
                )
            )
                .Should()
                .Be(0);
            await _ExecuteAsync(
                connection,
                null,
                $"UPDATE {_Table(table)} SET \"ContractVersion\" = @version;",
                ("@version", "V1")
            );
            (
                await _ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"ContractVersion\" = @version;",
                    ("@version", "v1")
                )
            )
                .Should()
                .Be(0);
        }

        await _ExecuteAsync(
            connection,
            null,
            $"UPDATE {_Table("CronJobs")} SET \"Request\" = @payload;",
            ("@payload", new byte[] { 42 })
        );
        await using var payloadCommand = connection.CreateCommand();
        payloadCommand.CommandText = $"SELECT \"Request\" FROM {_Table("CronJobOccurrences")};";
        ((byte[])(await payloadCommand.ExecuteScalarAsync(AbortToken))!).Should().Equal(payload);
    }

    public virtual async Task invalid_legacy_identity_aborts_before_any_schema_or_data_change()
    {
        // Supplementary characters count twice in the logical UTF-16 contract, even on PostgreSQL varchar.
        foreach (
            var invalidName in new[]
            {
                new string('x', 201),
                string.Concat(Enumerable.Repeat("\U0001F680", 101)),
                "padded ",
                "bad\tname",
            }
        )
        {
            await using var connection = fixture.CreateConnection();
            await connection.OpenAsync(AbortToken);
            await _CreateLegacyAsync(connection);
            await _SeedAsync(connection, invalidName, [1, 2, 3]);

            var upgrade = async () => await _UpgradeAsync(connection);
            await upgrade.Should().ThrowAsync<ArgumentException>();

            (
                await _ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM {_Table("CronJobs")} WHERE \"Function\" = @name;",
                    ("@name", invalidName)
                )
            )
                .Should()
                .Be(1);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {_Table("CronJobOccurrences")};";
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            reader.FieldCount.Should().Be(2, "preflight failure must leave the legacy shape intact");
            (await reader.ReadAsync(AbortToken)).Should().BeTrue();
        }
    }

    public virtual async Task supplementary_boundary_name_survives_without_truncation()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        var name = string.Concat(Enumerable.Repeat("\U0001F680", 100));
        name.Length.Should().Be(JobContract.NameMaxLength);
        await _SeedAsync(connection, name, null);
        await _UpgradeAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"Function\", \"Request\" FROM {_Table("CronJobOccurrences")};";
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        (await reader.ReadAsync(AbortToken)).Should().BeTrue();
        reader.GetString(0).Should().Be(name);
        (await reader.IsDBNullAsync(1, AbortToken)).Should().BeTrue("a requestless legacy job remains requestless");
    }

    public virtual async Task upgraded_schema_requires_explicit_nonblank_version_without_database_default()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        await _SeedAsync(connection, "Invoice.Send", null);
        await _UpgradeAsync(connection);

        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            foreach (var version in new string?[] { null, "", "   " })
            {
                var update = async () =>
                    await _ExecuteAsync(
                        connection,
                        null,
                        $"UPDATE {_Table(table)} SET \"ContractVersion\" = @version;",
                        ("@version", version)
                    );
                await update.Should().ThrowAsync<DbException>();
            }

            var parentColumn = table == "CronJobOccurrences" ? ", \"CronJobId\"" : string.Empty;
            var parentValue = table == "CronJobOccurrences" ? ", 1" : string.Empty;
            var insert = async () =>
                await _ExecuteAsync(
                    connection,
                    null,
                    $"INSERT INTO {_Table(table)} (\"Id\", \"Function\"{parentColumn}) VALUES (2, @name{parentValue});",
                    ("@name", "New.Writer")
                );
            await insert.Should().ThrowAsync<DbException>();
            (await _ScalarAsync(connection, $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"ContractVersion\" = '1';"))
                .Should()
                .Be(1);
        }
    }

    public virtual async Task downgrade_preflight_refuses_nonlegacy_contract_in_every_executable_table()
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(AbortToken);
        await _CreateLegacyAsync(connection);
        await _SeedAsync(connection, "Invoice.Send", null);
        await _UpgradeAsync(connection);
        await _PreflightDowngradeAsync(connection);

        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            await _ExecuteAsync(connection, null, $"UPDATE {_Table(table)} SET \"ContractVersion\" = '2';");
            var downgrade = async () => await _PreflightDowngradeAsync(connection);
            await downgrade.Should().ThrowAsync<InvalidOperationException>().WithMessage("*roll forward*");
            (await _ScalarAsync(connection, $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"ContractVersion\" = '2';"))
                .Should()
                .Be(1);
            await _ExecuteAsync(connection, null, $"UPDATE {_Table(table)} SET \"ContractVersion\" = '1';");
        }
    }

    private async Task _CreateLegacyAsync(DbConnection connection)
    {
        var create = IsPostgreSql ? "CREATE TEMPORARY TABLE" : "CREATE TABLE";
        var text = IsPostgreSql ? "text" : "nvarchar(max)";
        var bytes = IsPostgreSql ? "bytea" : "varbinary(max)";
        foreach (var table in new[] { "TimeJobs", "CronJobs" })
        {
            await _ExecuteAsync(
                connection,
                null,
                $"{create} {_Table(table)} (\"Id\" int PRIMARY KEY, \"Function\" {text} NOT NULL, \"Request\" {bytes} NULL);"
            );
        }

        await _ExecuteAsync(
            connection,
            null,
            $"{create} {_Table("CronJobOccurrences")} (\"Id\" int PRIMARY KEY, \"CronJobId\" int NOT NULL REFERENCES {_Table("CronJobs")} (\"Id\"));"
        );
    }

    private async Task _SeedAsync(DbConnection connection, string name, byte[]? payload)
    {
        foreach (var table in new[] { "TimeJobs", "CronJobs" })
        {
            await _ExecuteAsync(
                connection,
                null,
                $"INSERT INTO {_Table(table)} (\"Id\", \"Function\", \"Request\") VALUES (1, @name, @payload);",
                ("@name", name),
                ("@payload", payload)
            );
        }

        await _ExecuteAsync(
            connection,
            null,
            $"INSERT INTO {_Table("CronJobOccurrences")} (\"Id\", \"CronJobId\") VALUES (1, 1);"
        );
    }

    private async Task _UpgradeAsync(DbConnection connection)
    {
        // The consumer quiesces every writer before opening this transaction; a transaction alone is not that fence.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, AbortToken);
        foreach (var table in new[] { "TimeJobs", "CronJobs" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT \"Function\" FROM {_Table(table)};";
            await using var reader = await command.ExecuteReaderAsync(AbortToken);
            while (await reader.ReadAsync(AbortToken))
            {
                _ = new JobFunctionDescriptor(
                    reader.GetString(0),
                    null,
                    string.Empty,
                    JobPriority.Normal,
                    0,
                    JobContract.LegacyVersion
                );
            }
        }

        var text = IsPostgreSql ? "text" : "nvarchar(max)";
        var bytes = IsPostgreSql ? "bytea" : "varbinary(max)";
        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            await _ExecuteAsync(
                connection,
                transaction,
                $"ALTER TABLE {_Table(table)} ADD \"ContractVersion\" {text} NULL;"
            );
            await _ExecuteAsync(
                connection,
                transaction,
                $"ALTER TABLE {_Table(table)} ADD \"CorrelationId\" {text} NULL;"
            );
            await _ExecuteAsync(
                connection,
                transaction,
                $"ALTER TABLE {_Table(table)} ADD \"CausationId\" {text} NULL;"
            );
        }

        var occurrences = _Table("CronJobOccurrences");
        await _ExecuteAsync(connection, transaction, $"ALTER TABLE {occurrences} ADD \"Function\" {text} NULL;");
        await _ExecuteAsync(connection, transaction, $"ALTER TABLE {occurrences} ADD \"Request\" {bytes} NULL;");
        await _ExecuteAsync(connection, transaction, $"ALTER TABLE {occurrences} ADD \"TenantId\" {text} NULL;");
        await _ExecuteAsync(
            connection,
            transaction,
            $"UPDATE {occurrences} SET \"Function\" = (SELECT p.\"Function\" FROM {_Table("CronJobs")} p WHERE p.\"Id\" = {occurrences}.\"CronJobId\"), \"Request\" = (SELECT p.\"Request\" FROM {_Table("CronJobs")} p WHERE p.\"Id\" = {occurrences}.\"CronJobId\");"
        );

        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            await _ExecuteAsync(connection, transaction, $"UPDATE {_Table(table)} SET \"ContractVersion\" = '1';");
            await _ConstrainAsync(connection, transaction, table, "Function", JobContract.NameMaxLength);
            await _ConstrainAsync(connection, transaction, table, "ContractVersion", JobContract.VersionMaxLength);
        }

        await transaction.CommitAsync(AbortToken);
    }

    private async Task _ConstrainAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string column,
        int limit
    )
    {
        var qualified = _Table(table);
        var ddl = IsPostgreSql
            ? $"ALTER TABLE {qualified} ALTER COLUMN \"{column}\" TYPE varchar({limit}) COLLATE \"C\", ALTER COLUMN \"{column}\" SET NOT NULL;"
            : $"ALTER TABLE {qualified} ALTER COLUMN \"{column}\" nvarchar({limit}) COLLATE Latin1_General_100_BIN2 NOT NULL;";
        await _ExecuteAsync(connection, transaction, ddl);
        // Physical constraints reject missing/empty versions; the public .NET validator owns the full Unicode contract.
        await _ExecuteAsync(connection, transaction, $"ALTER TABLE {qualified} ADD CHECK (\"{column}\" <> '');");
        if (IsPostgreSql)
        {
            await _ExecuteAsync(
                connection,
                transaction,
                $"ALTER TABLE {qualified} ADD CHECK (btrim(\"{column}\") <> '');"
            );
        }
    }

    private async Task _PreflightDowngradeAsync(DbConnection connection)
    {
        foreach (var table in new[] { "TimeJobs", "CronJobs", "CronJobOccurrences" })
        {
            if (
                await _ScalarAsync(
                    connection,
                    $"SELECT COUNT(*) FROM {_Table(table)} WHERE \"ContractVersion\" <> '1' OR \"ContractVersion\" IS NULL;"
                ) != 0
            )
            {
                throw new InvalidOperationException(
                    "Non-legacy contracts exist; preserve the schema and roll forward."
                );
            }
        }
    }

    private static async Task _ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        _AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private static async Task<int> _ScalarAsync(
        DbConnection connection,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _AddParameters(command, parameters);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(AbortToken),
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static void _AddParameters(DbCommand command, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            if (name == "@payload")
            {
                parameter.DbType = DbType.Binary;
            }
            else
            {
                parameter.DbType = DbType.String;
            }

            command.Parameters.Add(parameter);
        }
    }
}
