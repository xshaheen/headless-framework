// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class DbContextMigrationsResourceKindTests : TestBase
{
    [Fact]
    public void should_report_the_public_kind_constant_that_a_dependent_provisioner_selects()
    {
        // given - the context is resolved only when the migrations run, so none is registered here
        var services = new ServiceCollection();
        services.AddDbContextMigrations<KindDbContext>();
        using var provider = services.BuildServiceProvider();

        // when
        var resource = provider.GetServices<IResourceProvisioner>().Should().ContainSingle().Which;

        // then
        // The value is what an operator passes to 'resources provision -k', so the constant must not change it.
        resource
            .Kind.Should()
            .Be(HeadlessDbContextMigrationsServiceCollectionExtensions.MigrationsResourceKind)
            .And.Be("EntityFramework");
    }

    private sealed class KindDbContext(DbContextOptions<KindDbContext> options) : DbContext(options);
}
