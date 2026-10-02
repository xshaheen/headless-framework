// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsModelParityTests(SqlServerPermissionsFixture fixture) : TestBase
{
    private const string _Schema = "permissions_sql_parity";

    [Fact]
    public async Task should_create_the_same_tables_columns_and_indexes_the_ef_model_maps()
    {
        // given — the raw schema contribution and the EF mapping must agree name for name, or an application that
        // provisions with one and reads with the other fails at its first query
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(AbortToken);
        await using var context = new ParityDbContext(
            new DbContextOptionsBuilder<ParityDbContext>().UseSqlServer(fixture.ConnectionString).Options,
            new PermissionsStorageOptions { Schema = _Schema }
        );

        // when
        var createdColumns = await _ReadPairsAsync(
            "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @schema"
        );
        var createdIndexes = await _ReadPairsAsync(
            """
            SELECT t.name, i.name
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND i.name IS NOT NULL
            """
        );
        var (mappedColumns, mappedIndexes) = _MappedObjects(context.Model);

        // then — the schema runner's history table sits beside the feature's tables but is no part of the EF model
        _WithoutHistory(createdColumns).Should().BeEquivalentTo(mappedColumns);
        _WithoutHistory(createdIndexes).Should().BeEquivalentTo(mappedIndexes);
    }

    private static IEnumerable<string> _WithoutHistory(IEnumerable<string> objects)
    {
        return objects.Where(x => !x.StartsWith(SchemaRunner.HistoryTableName + ".", StringComparison.Ordinal));
    }

    private static (HashSet<string> Columns, HashSet<string> Indexes) _MappedObjects(IModel model)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var indexes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in model.GetEntityTypes())
        {
            var tableName = entity.GetTableName()!;
            var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                columns.Add($"{tableName}.{property.GetColumnName(table)}");
            }

            indexes.Add($"{tableName}.{entity.FindPrimaryKey()!.GetName()}");

            foreach (var index in entity.GetIndexes())
            {
                indexes.Add($"{tableName}.{index.GetDatabaseName()}");
            }
        }

        return (columns, indexes);
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP TABLE [{_Schema}].[' + t.name + N'];'
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'{_Schema}';
            SELECT @sql += N'DROP TYPE [{_Schema}].[' + t.name + N'];'
            FROM sys.types t JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'{_Schema}' AND t.is_user_defined = 1;
            IF SCHEMA_ID(N'{_Schema}') IS NOT NULL SET @sql += N'DROP SCHEMA [{_Schema}];';
            EXEC sp_executesql @sql;
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    private async Task<HashSet<string>> _ReadPairsAsync(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", _Schema);
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        var pairs = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(AbortToken))
        {
            pairs.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }

        return pairs;
    }

    private sealed class ParityDbContext(DbContextOptions<ParityDbContext> options, PermissionsStorageOptions storage)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddHeadlessPermissions(storage, HeadlessStorageNaming.ForProvider(Database.ProviderName));
        }
    }
}
