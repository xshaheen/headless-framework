// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class AuditLogEntityValidationStartupGateTests : TestBase
{
    [Fact]
    public async Task should_reject_pre_registered_but_unconfigured_audit_log_entry()
    {
        // given
        var gate = new AuditLogEntityValidationStartupGate<PreRegisteredAuditLogDbContext>(
            new TestDbContextFactory<PreRegisteredAuditLogDbContext>(() =>
                new PreRegisteredAuditLogDbContext(_Options<PreRegisteredAuditLogDbContext>())
            )
        );

        // when
        var act = () => gate.StartingAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AddHeadlessAuditLog*");
    }

    [Fact]
    public async Task should_accept_fully_configured_audit_log_entry()
    {
        // given
        var gate = new AuditLogEntityValidationStartupGate<ConfiguredAuditLogDbContext>(
            new TestDbContextFactory<ConfiguredAuditLogDbContext>(() =>
                new ConfiguredAuditLogDbContext(_Options<ConfiguredAuditLogDbContext>())
            )
        );

        // when
        var act = () => gate.StartingAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
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
            modelBuilder.AddHeadlessAuditLog(new AuditLogStorageOptions());
        }
    }
}
