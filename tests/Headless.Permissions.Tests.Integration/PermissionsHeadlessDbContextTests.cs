// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting.Initialization;
using Headless.MultiTenancy;
using Headless.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tests.TestSetup;

namespace Tests;

/// <summary>
/// The EF permission-grant store over a <see cref="HeadlessDbContext"/>, registered pooled and per scope. Grants are
/// tenant-owned, so the Headless tenant filter applies on top of the store's own predicates: the ambient tenant and
/// the grant's tenant are the same here.
/// </summary>
public sealed class PermissionsHeadlessDbContextTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private bool _pooled;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_store_and_read_grants_through_a_headless_db_context(bool pooled)
    {
        // given
        _pooled = pooled;
        await Fixture.ResetAsync();
        using var host = CreateHost();
        host.Services.GetRequiredService<ICurrentTenant>().Id.Returns("t1");
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        var grant = new PermissionGrantRecord(Guid.NewGuid(), "Orders.Read", "User", "u1", isGranted: true, "t1");

        // when
        await repository.InsertAsync(grant, AbortToken);

        // then
        var stored = await repository.GetListAsync("User", "u1", AbortToken);
        stored.Should().ContainSingle().Which.Name.Should().Be("Orders.Read");
        host.Services.GetRequiredService<IDbContextFactory<HeadlessPermissionsDbContext>>()
            .Should()
            .Match(factory => (factory is PooledDbContextFactory<HeadlessPermissionsDbContext>) == pooled);
    }

    protected override void AddPermissionsDbContextFactory(IServiceCollection services)
    {
        if (_pooled)
        {
            services.AddHeadlessDbContextPool<HeadlessPermissionsDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
        else
        {
            services.AddHeadlessDbContext<HeadlessPermissionsDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
    }

    protected override void UsePermissionsEntityFramework(HeadlessPermissionsSetupBuilder setup)
    {
        setup.UseEntityFramework<HeadlessPermissionsDbContext>();
    }

    private sealed class HeadlessPermissionsDbContext(
        DbContextOptions<HeadlessPermissionsDbContext> options,
        IOptions<PermissionsStorageOptions> storageOptions
    ) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessPermissions(
                storageOptions.Value,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }
}
