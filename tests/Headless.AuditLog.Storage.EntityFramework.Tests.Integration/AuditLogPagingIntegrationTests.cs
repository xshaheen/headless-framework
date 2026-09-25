// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

public sealed class AuditLogPagingIntegrationTests : TestBase
{
    private static readonly DateTime _T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AuditLogSortDirection.NewestFirst)]
    [InlineData(AuditLogSortDirection.OldestFirst)]
    public async Task should_walk_every_entry_exactly_once_across_pages_in_keyset_order(AuditLogSortDirection direction)
    {
        // given — two entries share each timestamp so the Id tie-break decides order at page boundaries
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        var ids = await _SeedAsync(sp, Enumerable.Range(0, 7).Select(i => _Row($"e{i}", _T0.AddSeconds(i / 2))));
        var reader = sp.GetRequiredService<IReadAuditLog<AuditTestDbContext>>();

        // when
        var pages = new List<IReadOnlyList<AuditLogEntryData>>();
        string? token = null;

        do
        {
            var page = await reader.QueryAsync(
                new()
                {
                    Action = "paging.test",
                    Direction = direction,
                    Size = 3,
                    ContinuationToken = token,
                },
                AbortToken
            );
            pages.Add(page.Items);
            token = page.ContinuationToken;
        } while (token is not null && pages.Count < 10);

        // then
        pages.Select(p => p.Count).Should().Equal(3, 3, 1);
        var expected = direction == AuditLogSortDirection.NewestFirst ? ids.AsEnumerable().Reverse() : ids;
        pages.SelectMany(p => p).Select(e => e.EntityId).Should().Equal(expected);
    }

    [Fact]
    public async Task should_return_null_token_when_the_page_holds_the_last_entries_exactly()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await _SeedAsync(sp, Enumerable.Range(0, 3).Select(i => _Row($"e{i}", _T0.AddSeconds(i))));
        var reader = sp.GetRequiredService<IReadAuditLog<AuditTestDbContext>>();

        // when
        var page = await reader.QueryAsync(new() { Action = "paging.test", Size = 3 }, AbortToken);

        // then
        page.Items.Should().HaveCount(3);
        page.ContinuationToken.Should().BeNull();
        page.HasNext.Should().BeFalse();
        page.Size.Should().Be(3);
    }

    [Fact]
    public async Task should_filter_by_account_and_correlation_ids()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await _SeedAsync(
            sp,
            [
                _Row("match", _T0) with
                {
                    AccountId = "acc-1",
                    CorrelationId = "corr-1",
                },
                _Row("other-account", _T0) with
                {
                    AccountId = "acc-2",
                    CorrelationId = "corr-1",
                },
                _Row("other-correlation", _T0) with
                {
                    AccountId = "acc-1",
                    CorrelationId = "corr-2",
                },
            ]
        );
        var reader = sp.GetRequiredService<IReadAuditLog<AuditTestDbContext>>();

        // when
        var byAccount = await reader.QueryAsync(new() { AccountId = "acc-1" }, AbortToken);
        var byCorrelation = await reader.QueryAsync(new() { CorrelationId = "corr-1" }, AbortToken);
        var byBoth = await reader.QueryAsync(new() { AccountId = "acc-1", CorrelationId = "corr-1" }, AbortToken);

        // then
        byAccount.Items.Select(e => e.EntityId).Should().BeEquivalentTo("match", "other-correlation");
        byCorrelation.Items.Select(e => e.EntityId).Should().BeEquivalentTo("match", "other-account");
        byBoth.Items.Select(e => e.EntityId).Should().Equal("match");
    }

    [Fact]
    public async Task should_commit_writer_entry_without_flushing_pending_changes_of_the_scoped_context()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync();
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
        db.Orders.Add(
            new Order
            {
                Id = Guid.NewGuid(),
                CustomerName = "Pending",
                Email = "pending@example.com",
                Amount = 1m,
            }
        );
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter<AuditTestDbContext>>();

        // when
        await writer.WriteAsync(
            new AuditLogWriteRequest
            {
                Action = "authorization.forbidden",
                Success = false,
                Data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["route"] = "orders/{id}" },
            },
            AbortToken
        );

        // then
        await using var verify = await sp.GetRequiredService<IDbContextFactory<AuditTestDbContext>>()
            .CreateDbContextAsync(AbortToken);
        var entry = await verify.Set<AuditLogEntry>().SingleAsync(AbortToken);
        entry.Action.Should().Be("authorization.forbidden");
        entry.Success.Should().BeFalse();
        entry.UserId.Should().Be(AuditIntegrationFixture.UserId);
        entry.TenantId.Should().Be(AuditIntegrationFixture.TenantId);
        entry.NewValues.Should().ContainKey("route");
        (await verify.Orders.AnyAsync(AbortToken)).Should().BeFalse();
        db.ChangeTracker.Entries<Order>().Should().ContainSingle().Which.State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task should_skip_writer_entry_when_audit_log_is_disabled()
    {
        // given
        var (sp, conn) = await AuditIntegrationFixture.CreateAsync(options => options.IsEnabled = false);
        await using var _ = conn;
        await using var __ = sp;
        await using var scope = sp.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditLogWriter<AuditTestDbContext>>();

        // when
        await writer.WriteAsync(new AuditLogWriteRequest { Action = "authorization.forbidden" }, AbortToken);

        // then
        await using var verify = await sp.GetRequiredService<IDbContextFactory<AuditTestDbContext>>()
            .CreateDbContextAsync(AbortToken);
        (await verify.Set<AuditLogEntry>().AnyAsync(AbortToken)).Should().BeFalse();
    }

    private static AuditLogRow _Row(string entityId, DateTime createdAt)
    {
        return new AuditLogRow(entityId, createdAt, AccountId: null, CorrelationId: null);
    }

    // Inserts rows one at a time so identity values follow the list order; returns the entity IDs in that order.
    private async Task<List<string>> _SeedAsync(IServiceProvider sp, IEnumerable<AuditLogRow> rows)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<AuditTestDbContext>>();
        var ids = new List<string>();

        foreach (var row in rows)
        {
            await using var db = await factory.CreateDbContextAsync(AbortToken);
            db.Set<AuditLogEntry>()
                .Add(
                    new AuditLogEntry
                    {
                        Action = "paging.test",
                        EntityId = row.EntityId,
                        CreatedAt = row.CreatedAt,
                        AccountId = row.AccountId,
                        CorrelationId = row.CorrelationId,
                    }
                );
            await db.SaveChangesAsync(AbortToken);
            ids.Add(row.EntityId);
        }

        return ids;
    }

    private sealed record AuditLogRow(string EntityId, DateTime CreatedAt, string? AccountId, string? CorrelationId);
}
