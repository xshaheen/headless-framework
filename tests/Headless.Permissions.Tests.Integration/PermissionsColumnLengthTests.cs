// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionsColumnLengthTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private const string _Schema = "permissions_ef_lengths";
    private const string _PermissionName = "ColumnLengthPermission";
    private const string _ProviderName = "Workspace";

    // The shape a consumer app composes from two prefixed identifiers: 72 characters, past the default 64.
    private static readonly string _LongProviderKey = "ws_" + new string('a', 32) + ":usr_" + new string('b', 32);

    protected override void ConfigurePermissionsStorage(PermissionsStorageOptions options)
    {
        options.Schema = _Schema;
        options.PermissionGrantsProviderKeyMaxLength = 128;
    }

    protected override void AddPermissionsDbContextFactory(IServiceCollection services)
    {
        services.AddDbContextFactory<WideProviderKeyDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );
    }

    protected override void UsePermissionsEntityFramework(HeadlessPermissionsSetupBuilder setup)
    {
        setup.UseEntityFramework<WideProviderKeyDbContext>();
    }

    [Fact]
    public async Task should_grant_read_and_revoke_a_72_character_provider_key_when_its_column_is_widened_to_128()
    {
        // given — tables created from the EF model, as an application's migration would create them
        using var host = await _CreateHostWithTablesAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();

        // when
        await store.GrantAsync(_PermissionName, _ProviderName, _LongProviderKey, AbortToken);
        var granted = await store.IsGrantedAsync(_PermissionName, _ProviderName, _LongProviderKey, AbortToken);
        await store.RevokeAsync(_PermissionName, _ProviderName, _LongProviderKey, AbortToken);
        var revoked = await store.IsGrantedAsync(_PermissionName, _ProviderName, _LongProviderKey, AbortToken);

        // then
        _LongProviderKey.Should().HaveLength(72);
        granted.Should().Be(PermissionGrantStatus.Granted);
        revoked.Should().NotBe(PermissionGrantStatus.Granted);
    }

    private async Task<IHost> _CreateHostWithTablesAsync()
    {
        await Fixture.ResetAsync();
        var host = CreateHost(b => b.Services.AddPermissionDefinitionProvider<DefinitionProvider>());
        await using var scope = host.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WideProviderKeyDbContext>>();
        await using var db = await factory.CreateDbContextAsync(AbortToken);

        await db.Database.ExecuteSqlRawAsync($"""DROP SCHEMA IF EXISTS "{_Schema}" CASCADE""", AbortToken);
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{_Schema}\"", AbortToken);
        await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);

        return host;
    }

    // EF caches one model per context type, so the widened column needs a context type of its own.
    private sealed class WideProviderKeyDbContext(
        DbContextOptions<WideProviderKeyDbContext> options,
        IOptions<PermissionsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureHeadlessPermissions(
                storageOptions.Value,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }

    [UsedImplicitly]
    private sealed class DefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("ColumnLengthGroup").AddChild(_PermissionName);
        }
    }
}
