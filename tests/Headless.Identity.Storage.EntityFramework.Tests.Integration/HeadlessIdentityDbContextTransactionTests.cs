// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Entities;
using Tests.Fixture;

namespace Tests;

/// <summary>
/// Proves a unit of work binds to and commits on a <c>HeadlessIdentityDbContext</c> — which implements
/// <c>IHeadlessDbContext</c> but derives from <c>IdentityDbContext</c>, not <c>HeadlessDbContext</c>. The
/// Identity context reaches the unit-of-work machinery only through that seam, so this guards the
/// <c>IHeadlessDbContext</c>-based wiring in the save pipeline against a regression to the concrete
/// <c>HeadlessDbContext</c> (which would silently exclude the Identity context).
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
    public async Task unit_of_work_commits_on_identity_context()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var entity = new HarnessTestEntity { Name = "unit-of-work", TenantId = "T1" };

        // when — RunAsync begins a unit of work on the Identity context, runs the operation, and completes.
        // The inner save resolves the unit through the `IHeadlessDbContext` seam, not the concrete
        // `HeadlessDbContext`, which is what this test pins.
        await unitOfWorkManager.RunAsync(
            db,
            async (unitOfWork, ct) =>
            {
                unitOfWork.Resource!.IsOwned.Should().BeTrue("RunAsync owns the transaction it began");
                db.Database.CurrentTransaction.Should().NotBeNull("the unit's transaction is the context's own");

                db.TestEntities.Add(entity);
                await db.SaveChangesAsync(ct);
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
