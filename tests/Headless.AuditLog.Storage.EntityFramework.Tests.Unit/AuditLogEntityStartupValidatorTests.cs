// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AuditLogEntityStartupValidatorTests : TestBase
{
    [Fact]
    public async Task should_reject_pre_registered_but_unconfigured_audit_log_entry()
    {
        // given
        await using var services = _Services<PreRegisteredAuditLogDbContext>(() =>
            new PreRegisteredAuditLogDbContext(_Options<PreRegisteredAuditLogDbContext>())
        );
        var validator = new AuditLogEntityStartupValidator<PreRegisteredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ConfigureHeadlessAuditLog*");
    }

    [Fact]
    public async Task should_accept_fully_configured_audit_log_entry()
    {
        // given
        await using var services = _Services<ConfiguredAuditLogDbContext>(() =>
            new ConfiguredAuditLogDbContext(_Options<ConfiguredAuditLogDbContext>())
        );
        var validator = new AuditLogEntityStartupValidator<ConfiguredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_leave_a_missing_factory_to_the_required_service_check()
    {
        // given — no factory registered: the required-service check reports it, so this check must not fail first
        await using var services = new ServiceCollection().BuildServiceProvider();
        var validator = new AuditLogEntityStartupValidator<ConfiguredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_reject_the_owner_purge_authority_on_a_database_other_than_postgresql()
    {
        // given - only PostgreSQL has the owner's purge function, so the retention could never purge
        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<ConfiguredAuditLogDbContext>>(
                new TestDbContextFactory<ConfiguredAuditLogDbContext>(() =>
                    new ConfiguredAuditLogDbContext(_Options<ConfiguredAuditLogDbContext>())
                )
            )
            .Configure<AuditLogStorageOptions>(options => options.PurgeAuthority = AuditLogPurgeAuthority.Owner)
            .BuildServiceProvider();
        var validator = new AuditLogEntityStartupValidator<ConfiguredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PurgeAuthority*PostgreSQL only*");
    }

    [Theory]
    [InlineData(StorageProvider.PostgreSql, "UsePostgreSqlEntityFramework<TContext>()")]
    [InlineData(StorageProvider.SqlServer, "UseSqlServerEntityFramework<TContext>()")]
    public async Task should_reject_a_registration_made_for_another_database(
        StorageProvider registered,
        string registration
    )
    {
        // given - that database's purge and helpers would run against a database that does not know them
        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<ConfiguredAuditLogDbContext>>(
                new TestDbContextFactory<ConfiguredAuditLogDbContext>(() =>
                    new ConfiguredAuditLogDbContext(_Options<ConfiguredAuditLogDbContext>())
                )
            )
            .AddSingleton(new EfAuditLogDatabase(registered))
            .BuildServiceProvider();
        var validator = new AuditLogEntityStartupValidator<ConfiguredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then - it names what was registered and what the context's database needs
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(registration)
            .And.Contain("Microsoft.EntityFrameworkCore.Sqlite")
            .And.Contain("Register it with UseEntityFramework<TContext>()");
    }

    [Fact]
    public async Task should_accept_the_provider_neutral_registration_on_a_database_without_its_own_package()
    {
        // given
        await using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<ConfiguredAuditLogDbContext>>(
                new TestDbContextFactory<ConfiguredAuditLogDbContext>(() =>
                    new ConfiguredAuditLogDbContext(_Options<ConfiguredAuditLogDbContext>())
                )
            )
            .AddSingleton(new EfAuditLogDatabase(Registered: null))
            .BuildServiceProvider();
        var validator = new AuditLogEntityStartupValidator<ConfiguredAuditLogDbContext>(services);

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    private static ServiceProvider _Services<TContext>(Func<TContext> createContext)
        where TContext : DbContext
    {
        return new ServiceCollection()
            .AddSingleton<IDbContextFactory<TContext>>(new TestDbContextFactory<TContext>(createContext))
            .BuildServiceProvider();
    }

    private static DbContextOptions<TContext> _Options<TContext>()
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options;
    }

    private sealed class TestDbContextFactory<TContext>(Func<TContext> createContext) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext()
        {
            return createContext();
        }
    }

    private sealed class PreRegisteredAuditLogDbContext(DbContextOptions<PreRegisteredAuditLogDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var auditLogEntry = modelBuilder.Entity<AuditLogEntry>();
            auditLogEntry.HasKey(entry => new { entry.CreatedAt, entry.Id });
            auditLogEntry.Ignore(entry => entry.OldValues);
            auditLogEntry.Ignore(entry => entry.NewValues);
            auditLogEntry.Ignore(entry => entry.ChangedFields);
        }
    }

    private sealed class ConfiguredAuditLogDbContext(DbContextOptions<ConfiguredAuditLogDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessAuditLog(new AuditLogStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
