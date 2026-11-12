// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class PostgreSqlEntityFrameworkAuditLogSetupTests : TestBase
{
    [Fact]
    public async Task should_register_the_storage_with_the_postgresql_retention_and_partitions()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AuditDbContext>(_UseNpgsql);

        // when
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSqlEntityFramework<AuditDbContext>());
        await using var provider = services.BuildServiceProvider();

        // then - the purge passes the append-only trigger, and the partitions are kept, only through these two
        provider
            .GetRequiredService<IAuditLogRetention<AuditDbContext>>()
            .GetType()
            .Name.Should()
            .StartWith("PostgreSqlEfAuditLogRetention");
        provider
            .GetRequiredService<IAuditLogPartitions<AuditDbContext>>()
            .GetType()
            .Name.Should()
            .StartWith("PostgreSqlEfAuditLogPartitions");
        provider.GetRequiredService<IReadAuditLog<AuditDbContext>>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_refuse_a_retention_period_that_is_not_positive_before_reaching_the_database()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AuditDbContext>(_UseNpgsql);
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSqlEntityFramework<AuditDbContext>());
        await using var provider = services.BuildServiceProvider();
        var partitions = provider.GetRequiredService<IAuditLogPartitions<AuditDbContext>>();

        // when
        var act = () => partitions.DropExpiredAsync(TimeSpan.Zero, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_pass_the_startup_check_for_a_context_on_postgresql()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(_UseNpgsql);
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSqlEntityFramework<AuditDbContext>());

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures.Should().NotContain(failure => failure is InvalidOperationException);
    }

    [Fact]
    public async Task should_refuse_startup_when_the_provider_neutral_registration_is_used_on_postgresql()
    {
        // given - that registration purges without passing the append-only trigger and keeps no partitions
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(_UseNpgsql);
        services.AddHeadlessAuditLog(setup => setup.UseEntityFramework<AuditDbContext>());

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then - the failure names the registration to use
        failures
            .OfType<InvalidOperationException>()
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("UsePostgreSqlEntityFramework<TContext>()")
            .And.Contain("Headless.AuditLog.Storage.PostgreSql.EntityFramework");
    }

    [Fact]
    public async Task should_refuse_startup_when_the_context_runs_on_another_database()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddHeadlessAuditLog(setup => setup.UsePostgreSqlEntityFramework<AuditDbContext>());

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures
            .OfType<InvalidOperationException>()
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Microsoft.EntityFrameworkCore.Sqlite")
            .And.Contain("UseEntityFramework<TContext>()");
    }

    private static void _UseNpgsql(DbContextOptionsBuilder options)
    {
        // Never opened: the checks read the provider name from the options.
        options.UseNpgsql("Host=localhost;Database=audit");
    }

    private static async Task<List<Exception>> _RunStartupValidatorsAsync(ServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        var failures = new List<Exception>();

        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            try
            {
                await validator.ValidateAsync(AbortToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessAuditLog(
                new AuditLogStorageOptions(),
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }
}
