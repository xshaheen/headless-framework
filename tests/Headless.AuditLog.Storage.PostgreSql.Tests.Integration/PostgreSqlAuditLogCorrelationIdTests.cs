// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Context;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A job or message carries a business correlation id of up to 200 characters, and an audit row written inside it must
/// keep that id whole, so a query by it finds the row, on raw and on Entity Framework storage.
/// </summary>
[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogCorrelationIdTests(PostgreSqlAuditLogFixture fixture) : TestBase
{
    private const string _RawSchema = "audit_log_pg_correlation";
    private const string _EfSchema = "audit_log_pg_ef_correlation";

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, false)]
    [InlineData(200, true)]
    [InlineData(201, true)]
    public async Task should_keep_up_to_200_characters_of_the_correlation_id_with_raw_storage(
        int length,
        bool throughWriter
    )
    {
        // given
        var correlationId = new string('c', length);
        var stored = correlationId[..Math.Min(length, 200)];
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""DROP SCHEMA IF EXISTS "{_RawSchema}" CASCADE;""",
            AbortToken
        );
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ICorrelationIdProvider>(new FixedCorrelationIdProvider(correlationId));
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _RawSchema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        await using var scope = host.Services.CreateAsyncScope();

        // when
        if (throughWriter)
        {
            await scope
                .ServiceProvider.GetRequiredService<IAuditLogWriter<object>>()
                .WriteAsync(new AuditLogWriteRequest { Action = "correlation.kept" }, AbortToken);
        }
        else
        {
            await scope
                .ServiceProvider.GetRequiredService<IAuditLogStore>()
                .SaveAsync(
                    [
                        new AuditLogEntryData
                        {
                            Action = "correlation.kept",
                            CorrelationId = correlationId,
                            CreatedAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero),
                        },
                    ],
                    savingContext: new object(),
                    AbortToken
                );
        }

        var reader = scope.ServiceProvider.GetRequiredService<IReadAuditLog<object>>();
        var byStored = await reader.QueryAsync(new() { CorrelationId = stored }, cancellationToken: AbortToken);
        // A query by the id as the caller had it finds the row only when it fit the column: a longer id is compared
        // whole, so it never matches the row that stores its cut prefix.
        var byFull = await reader.QueryAsync(new() { CorrelationId = correlationId }, cancellationToken: AbortToken);

        // then
        byStored.Items.Should().ContainSingle().Which.CorrelationId.Should().Be(stored);
        byFull.Items.Should().HaveCount(length > 200 ? 0 : 1);
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(201, false)]
    [InlineData(200, true)]
    [InlineData(201, true)]
    public async Task should_keep_up_to_200_characters_of_the_correlation_id_with_entity_framework_storage(
        int length,
        bool throughWriter
    )
    {
        // given
        var correlationId = new string('c', length);
        var stored = correlationId[..Math.Min(length, 200)];
        await using var services = _CreateEfServices(correlationId);
        var factory = services.GetRequiredService<IDbContextFactory<CorrelationAuditDbContext>>();
        await PostgreSqlTestDatabase.ExecuteAsync(
            fixture.ConnectionString,
            $"""DROP SCHEMA IF EXISTS "{_EfSchema}" CASCADE; CREATE SCHEMA "{_EfSchema}";""",
            AbortToken
        );

        await using (var context = await factory.CreateDbContextAsync(AbortToken))
        {
            await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
        }

        // when
        await using (var scope = services.CreateAsyncScope())
        {
            if (throughWriter)
            {
                await scope
                    .ServiceProvider.GetRequiredService<IAuditLogWriter<CorrelationAuditDbContext>>()
                    .WriteAsync(new AuditLogWriteRequest { Action = "correlation.kept" }, AbortToken);
            }
            else
            {
                await using var context = await factory.CreateDbContextAsync(AbortToken);
                await scope
                    .ServiceProvider.GetRequiredService<IAuditLogStore>()
                    .SaveAsync(
                        [
                            new AuditLogEntryData
                            {
                                Action = "correlation.kept",
                                CorrelationId = correlationId,
                                CreatedAt = new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero),
                            },
                        ],
                        savingContext: context,
                        AbortToken
                    );
                await context.SaveChangesAsync(AbortToken);
            }
        }

        var reader = services.GetRequiredService<IReadAuditLog<CorrelationAuditDbContext>>();
        var byStored = await reader.QueryAsync(new() { CorrelationId = stored }, cancellationToken: AbortToken);
        // A query by the id as the caller had it finds the row only when it fit the column: a longer id is compared
        // whole, so it never matches the row that stores its cut prefix.
        var byFull = await reader.QueryAsync(new() { CorrelationId = correlationId }, cancellationToken: AbortToken);

        // then
        byStored.Items.Should().ContainSingle().Which.CorrelationId.Should().Be(stored);
        byFull.Items.Should().HaveCount(length > 200 ? 0 : 1);
    }

    private ServiceProvider _CreateEfServices(string correlationId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICorrelationIdProvider>(new FixedCorrelationIdProvider(correlationId));
        services.AddSingleton<ICurrentUser, NullCurrentUser>();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddDbContextFactory<CorrelationAuditDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = _EfSchema);
            setup.UsePostgreSqlEntityFramework<CorrelationAuditDbContext>();
        });

        return services.BuildServiceProvider();
    }

    private sealed class FixedCorrelationIdProvider(string correlationId) : ICorrelationIdProvider
    {
        public string? CorrelationId => correlationId;
    }

    public sealed class CorrelationAuditDbContext(DbContextOptions<CorrelationAuditDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            AuditLogMigrationScripts.MapAuditLog(modelBuilder, _EfSchema);
        }
    }
}
