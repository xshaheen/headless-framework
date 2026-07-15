// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Fixture;
using Tests.Tests;

namespace Tests;

/// <summary>
/// Integration tests for HeadlessIdentityDbContext global query filter behavior.
/// Inherits from harness base to verify Identity context has same filtering as HeadlessDbContext.
/// </summary>
[Collection<IdentityTestFixture>]
public sealed class HeadlessIdentityDbContextGlobalFiltersTests(IdentityTestFixture fixture)
    : HeadlessDbContextGlobalFiltersTestBase<IdentityTestFixture, TestIdentityDbContext>(fixture)
{
    private readonly IdentityTestFixture _fixture = fixture;

    [Fact]
    public void should_default_identity_schema_to_version3_when_add_headless_identity_db_context()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(_ => { });

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        options.Stores.SchemaVersion.Should().Be(IdentitySchemaVersions.Version3);
    }

    [Fact]
    public void should_allow_later_schema_override_when_add_headless_identity_db_context()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(_ => { });

        // when
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version2);

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        options.Stores.SchemaVersion.Should().Be(IdentitySchemaVersions.Version2);
    }

    [Fact]
    public void should_register_headless_db_context_services_when_add_headless_identity_db_context()
    {
        // given — HeadlessIdentityDbContext's constructor depends on HeadlessDbContextServices
        // (scoped). The Identity setup must wire this through SetupEntityFramework so consumers
        // who only call AddHeadlessDbContext (Identity overload) get a constructible context.
        // Regression guard: before this fix, Identity setup called bare AddDbContext only and
        // tests passed only because the test fixture wired HeadlessDbContextServices separately.
        var services = new ServiceCollection();
        services.AddHeadlessDbContext<
            TestIdentityDbContext,
            TestUser,
            TestRole,
            string,
            IdentityUserClaim<string>,
            IdentityUserRole<string>,
            IdentityUserLogin<string>,
            IdentityRoleClaim<string>,
            IdentityUserToken<string>,
            IdentityUserPasskey<string>
        >(_ => { });

        // then — HeadlessDbContextServices must be in the descriptor list
        var hasHeadlessServices = services.Any(d => d.ServiceType == typeof(HeadlessDbContextServices));
        hasHeadlessServices
            .Should()
            .BeTrue("Identity setup must wire HeadlessDbContextServices via AddHeadlessDbContextServices");
    }

    [Fact]
    public async Task should_include_passkey_entity_by_default_when_headless_identity_db_context()
    {
        // given
        await using var scope = _fixture.ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestIdentityDbContext>();

        // when
        var passkeyEntity = db.Model.FindEntityType(typeof(IdentityUserPasskey<string>));

        // then
        passkeyEntity.Should().NotBeNull();
        passkeyEntity!.GetTableName().Should().Be("AspNetUserPasskeys");
    }
}
