// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SqlServerEntityFrameworkAuditLogSetupTests : TestBase
{
    [Fact]
    public async Task should_register_the_storage_with_the_sql_server_retention_and_no_partitions()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AuditDbContext>(_UseSqlServer);

        // when
        services.AddHeadlessAuditLog(setup => setup.UseSqlServerEntityFramework<AuditDbContext>());
        await using var provider = services.BuildServiceProvider();

        // then - partitioning is PostgreSQL's, so nothing here offers it
        provider
            .GetRequiredService<IAuditLogRetention<AuditDbContext>>()
            .GetType()
            .Name.Should()
            .StartWith("SqlServerEfAuditLogRetention");
        provider.GetService<IAuditLogPartitions<AuditDbContext>>().Should().BeNull();
        provider.GetRequiredService<IReadAuditLog<AuditDbContext>>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_pass_the_startup_check_for_a_context_on_sql_server()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(_UseSqlServer);
        services.AddHeadlessAuditLog(setup => setup.UseSqlServerEntityFramework<AuditDbContext>());

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures.Should().NotContain(failure => failure is InvalidOperationException);
    }

    [Fact]
    public async Task should_refuse_startup_when_the_provider_neutral_registration_is_used_on_sql_server()
    {
        // given - that registration purges without passing the append-only trigger
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(_UseSqlServer);
        services.AddHeadlessAuditLog(setup => setup.UseEntityFramework<AuditDbContext>());

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then - the failure names the registration to use
        failures
            .OfType<InvalidOperationException>()
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("UseSqlServerEntityFramework<TContext>()")
            .And.Contain("Headless.AuditLog.Storage.SqlServer.EntityFramework");
    }

    [Fact]
    public async Task should_refuse_the_owner_purge_authority_because_only_postgresql_has_it()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuditDbContext>(_UseSqlServer);
        services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.PurgeAuthority = AuditLogPurgeAuthority.Owner);
            setup.UseSqlServerEntityFramework<AuditDbContext>();
        });

        // when
        var failures = await _RunStartupValidatorsAsync(services);

        // then
        failures
            .OfType<InvalidOperationException>()
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Match("*PurgeAuthority*PostgreSQL only*");
    }

    private static void _UseSqlServer(DbContextOptionsBuilder options)
    {
        // Never opened: the checks read the provider name from the options.
        options.UseSqlServer("Server=localhost;Database=audit;TrustServerCertificate=true");
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
