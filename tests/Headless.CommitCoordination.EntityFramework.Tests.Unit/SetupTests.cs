// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests
{
    [Fact]
    public void should_attach_commit_interceptor_once_to_a_plain_application_context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationContext>(options => options.UseSqlite("DataSource=:memory:"));
        services.AddEntityFrameworkCommitCoordination<ApplicationContext>();
        services.AddEntityFrameworkCommitCoordination<ApplicationContext>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        context
            .GetService<IDbContextOptions>()
            .Extensions.OfType<CoreOptionsExtension>()
            .SelectMany(extension => extension.Interceptors ?? [])
            .OfType<CommitCoordinationTransactionInterceptor>()
            .Should()
            .ContainSingle();
    }

    private sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options);

    [Fact]
    public void should_register_entity_framework_signal_source()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkCommitCoordination();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Should().NotBeNull();
        provider.GetRequiredService<EntityFrameworkCommitSignalSource>().Should().NotBeNull();
    }
}
