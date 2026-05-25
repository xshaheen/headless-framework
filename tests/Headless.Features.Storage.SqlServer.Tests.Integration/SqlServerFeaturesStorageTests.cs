// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Features;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerFeaturesFixture>]
public sealed class SqlServerFeaturesStorageTests(SqlServerFeaturesFixture fixture)
{
    private const string _Schema = "features_sql_raw";

    [Fact]
    public async Task should_initialize_tables_and_round_trip_feature_value_and_definition()
    {
        // given
        await _DropSchemaAsync();
        using var host = _CreateHost();

        // when
        await host.StartAsync(TestContext.Current.CancellationToken);
        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        var valueRepository = host.Services.GetRequiredService<IFeatureValueRecordRepository>();
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();
        var record = new FeatureValueRecord(Guid.NewGuid(), "Checkout.Enabled", "true", "Edition", "pro");
        var group = new FeatureGroupDefinitionRecord(Guid.NewGuid(), "Checkout", "Checkout");
        var feature = new FeatureDefinitionRecord(Guid.NewGuid(), "Checkout", "Checkout.Enabled", null, "Checkout enabled");

        await valueRepository.InsertAsync(record, TestContext.Current.CancellationToken);
        await definitionRepository.SaveAsync(
            [group],
            [],
            [],
            [feature],
            [],
            [],
            TestContext.Current.CancellationToken
        );
        var stored = await valueRepository.FindAsync("Checkout.Enabled", "Edition", "pro", TestContext.Current.CancellationToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(TestContext.Current.CancellationToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        (await _TableExistsAsync("FeatureValues")).Should().BeTrue();
        (await _TableExistsAsync("FeatureDefinitions")).Should().BeTrue();
        (await _TableExistsAsync("FeatureGroupDefinitions")).Should().BeTrue();
        stored.Should().NotBeNull();
        stored!.Value.Should().Be("true");
        storedGroups.Should().ContainSingle(x => x.Name == "Checkout");
        storedFeatures.Should().ContainSingle(x => x.Name == "Checkout.Enabled");
    }

    [Fact]
    public async Task should_persist_all_definitions_across_multiple_chunks_when_batch_exceeds_chunk_size()
    {
        // given — chunk size is 100 rows; 150 forces two chunks
        await _DropSchemaAsync();
        using var host = _CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var definitionRepository = host.Services.GetRequiredService<IFeatureDefinitionRecordRepository>();

        const int totalGroups = 150;
        const int totalFeatures = 150;
        var groups = Enumerable.Range(0, totalGroups)
            .Select(i => new FeatureGroupDefinitionRecord(Guid.NewGuid(), $"Group_{i:D4}", $"Group {i}"))
            .ToList();
        var features = Enumerable.Range(0, totalFeatures)
            .Select(i => new FeatureDefinitionRecord(Guid.NewGuid(), groups[i % totalGroups].Name, $"Feature_{i:D4}", null, $"Feature {i}"))
            .ToList();

        // when
        await definitionRepository.SaveAsync(groups, [], [], features, [], [], TestContext.Current.CancellationToken);
        var storedGroups = await definitionRepository.GetGroupsListAsync(TestContext.Current.CancellationToken);
        var storedFeatures = await definitionRepository.GetFeaturesListAsync(TestContext.Current.CancellationToken);

        // then
        storedGroups.Should().HaveCount(totalGroups);
        storedFeatures.Should().HaveCount(totalFeatures);
        storedGroups.Select(g => g.Name).Should().BeEquivalentTo(groups.Select(g => g.Name));
        storedFeatures.Select(f => f.Name).Should().BeEquivalentTo(features.Select(f => f.Name));
    }

    private IHost _CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _Schema);
            setup.UseSqlServer(fixture.ConnectionString);
        });

        return builder.Build();
    }

    private async Task _DropSchemaAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            $"""
            IF OBJECT_ID(N'{_Schema}.FeatureValues', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureValues];
            IF OBJECT_ID(N'{_Schema}.FeatureDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureDefinitions];
            IF OBJECT_ID(N'{_Schema}.FeatureGroupDefinitions', N'U') IS NOT NULL DROP TABLE [{_Schema}].[FeatureGroupDefinitions];
            IF EXISTS (SELECT * FROM sys.schemas WHERE name = N'{_Schema}') EXEC(N'DROP SCHEMA [{_Schema}]');
            """,
            connection
        );
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> _TableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
            """,
            connection
        );
        command.Parameters.AddWithValue("@schema", _Schema);
        command.Parameters.AddWithValue("@table", tableName);

        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
