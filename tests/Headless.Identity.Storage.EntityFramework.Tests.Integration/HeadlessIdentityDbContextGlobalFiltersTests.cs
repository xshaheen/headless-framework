// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
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
public sealed class HeadlessIdentityDbContextGlobalFiltersTests
    : HeadlessDbContextGlobalFiltersTestBase<IdentityTestFixture, TestIdentityDbContext>
{
    private readonly IdentityTestFixture _fixture;

    public HeadlessIdentityDbContextGlobalFiltersTests(IdentityTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void add_headless_identity_db_context_should_default_identity_schema_to_version3()
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
    public void add_headless_identity_db_context_should_allow_later_schema_override()
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
        services.Configure<IdentityOptions>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
        });

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        options.Stores.SchemaVersion.Should().Be(IdentitySchemaVersions.Version2);
    }

    [Fact]
    public async Task headless_identity_db_context_should_include_passkey_entity_by_default()
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
