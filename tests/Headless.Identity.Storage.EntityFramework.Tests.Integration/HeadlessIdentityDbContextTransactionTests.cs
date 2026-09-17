// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Entities;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// Proves the scope-free <c>ExecuteTransactionAsync</c> binds to and runs on a <c>HeadlessIdentityDbContext</c> —
/// which implements <c>IHeadlessDbContext</c> but derives from <c>IdentityDbContext</c>, not
/// <c>HeadlessDbContext</c>. Guards the generalized <c>where TContext : DbContext, IHeadlessDbContext</c>
/// receiver against a regression back to the concrete <c>HeadlessDbContext</c> (which would silently exclude the
/// Identity context at compile time).
/// </summary>
[Collection<IdentityTestFixture>]
public sealed class HeadlessIdentityDbContextTransactionTests : TestBase
{
    private readonly IdentityTestFixture _fixture;

    public HeadlessIdentityDbContextTransactionTests(IdentityTestFixture fixture)
    {
        _fixture = fixture;

        // Clean DB per test (shared collection fixture).
        using var scope = fixture.ServiceProvider.CreateScope();
        scope.ServiceProvider.EnsureDbRecreated<TestIdentityDbContext>();
    }

    [Fact]
    public async Task scope_free_unit_of_work_commits_on_identity_context()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        var entity = new HarnessTestEntity { Name = "unit-of-work", TenantId = "T1" };

        // when — the scope-free helper begins a unit of work on the Identity context (self-sourcing the scoped
        // manager), runs the operation, and completes. This compiles only because the receiver targets
        // `IHeadlessDbContext`, not the concrete `HeadlessDbContext`.
        await db.ExecuteTransactionAsync(
            async (context, ct) =>
            {
                context.TestEntities.Add(entity);
                await context.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        // then — the save pipeline generated the key and the row is durably committed (a fresh context sees it).
        // IgnoreQueryFilters bypasses the multi-tenant/soft-delete global filters: this asserts commit
        // durability, not tenancy.
        entity.Id.Should().NotBe(Guid.Empty);

        await using var verifyScope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        (await verifyDb.TestEntities.IgnoreQueryFilters().CountAsync(AbortToken)).Should().Be(1);
    }
}
