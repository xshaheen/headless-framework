// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

public sealed class UnitOfWorkFactoryPostgreSqlExtensionsTests : TestBase
{
    [Fact]
    public void should_register_the_scoped_manager_once_and_idempotently()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlUnitOfWork();
        services.AddPostgreSqlUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkFactory)).Should().Be(1);
        services
            .Single(d => d.ServiceType == typeof(IUnitOfWorkFactory))
            .Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void should_resolve_from_the_root_and_from_a_scope_as_one_instance()
    {
        using var provider = new ServiceCollection()
            .AddPostgreSqlUnitOfWork()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var fromRoot = provider.GetRequiredService<IUnitOfWorkFactory>();

        scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().Should().BeSameAs(fromRoot);
    }

    [Fact]
    public async Task should_validate_arguments_before_touching_the_connection()
    {
        await using var provider = new ServiceCollection().AddPostgreSqlUnitOfWork().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new NpgsqlConnection("Host=localhost;Database=unused");

        var beginAct = () => manager.BeginAsync((NpgsqlConnection)null!, cancellationToken: AbortToken).AsTask();
        var beginNull = (await beginAct.Should().ThrowAsync<ArgumentNullException>()).Which;

        var enlistAct = () => manager.Enlist(connection, null!);
        var enlistNull = enlistAct.Should().Throw<ArgumentNullException>().Which;

        var runNull = (
            await FluentActions
                .Awaiting(() =>
                    manager.RunAsync(
                        connection,
                        (Func<IUnitOfWork, CancellationToken, Task>)null!,
                        cancellationToken: AbortToken
                    )
                )
                .Should()
                .ThrowAsync<ArgumentNullException>()
        ).Which;

        beginNull.ParamName.Should().Be("connection");
        enlistNull.ParamName.Should().Be("transaction");
        runNull.ParamName.Should().Be("operation");
    }
}
