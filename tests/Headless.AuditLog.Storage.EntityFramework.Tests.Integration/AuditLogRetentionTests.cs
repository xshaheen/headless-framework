// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Fixture;

namespace Tests;

public sealed class AuditLogRetentionTests : TestBase
{
    [Fact]
    public async Task should_purge_only_expired_entries_oldest_first_up_to_the_limit()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        var now = AuditIntegrationFixture.Now.UtcDateTime;
        await _AddAsync(sp, ("expired.first", now.AddDays(-60)), ("expired.second", now.AddDays(-59)), ("recent", now));
        var retention = sp.GetRequiredService<IAuditLogRetention<AuditTestDbContext>>();

        // when
        var first = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 1, AbortToken);
        var remainingAfterFirst = await _ActionsAsync(sp);
        var second = await retention.PurgeAsync(TimeSpan.FromDays(30), limit: 10, AbortToken);

        // then
        first.Should().Be(1);
        remainingAfterFirst.Should().Equal("expired.second", "recent");
        second.Should().Be(1);
        (await _ActionsAsync(sp)).Should().Equal("recent");
    }

    [Fact]
    public async Task should_reject_a_non_positive_retention_or_limit()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        var retention = sp.GetRequiredService<IAuditLogRetention<AuditTestDbContext>>();

        // when
        var zeroAge = () => retention.PurgeAsync(TimeSpan.Zero, limit: 1, AbortToken);
        var zeroLimit = () => retention.PurgeAsync(TimeSpan.FromDays(1), limit: 0, AbortToken);

        // then
        await zeroAge.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_reject_append_only_because_migrations_own_the_table()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync(
            configure: null,
            configureServices: services =>
                services.Configure<AuditLogStorageOptions>(options => options.AppendOnly = true)
        );
        await using var _ = conn;
        await using var __ = sp;

        // when
        var act = () => sp.GetRequiredService<IOptions<AuditLogStorageOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*AppendOnly*migration*");
    }

    private static async Task _AddAsync(IServiceProvider sp, params (string Action, DateTime CreatedAt)[] entries)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        foreach (var (action, createdAt) in entries)
        {
            db.Set<AuditLogEntry>().Add(new AuditLogEntry { Action = action, CreatedAt = createdAt });
        }

        await db.SaveChangesAsync(AbortToken);
    }

    private static async Task<List<string>> _ActionsAsync(IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();

        return await db.Set<AuditLogEntry>()
            .AsNoTracking()
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.Id)
            .Select(entry => entry.Action)
            .ToListAsync(AbortToken);
    }
}
