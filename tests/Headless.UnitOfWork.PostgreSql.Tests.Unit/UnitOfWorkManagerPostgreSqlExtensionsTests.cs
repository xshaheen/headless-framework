// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

public sealed class UnitOfWorkManagerPostgreSqlExtensionsTests : TestBase
{
    [Fact]
    public void should_register_the_scoped_manager_once_and_idempotently()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlUnitOfWork();
        services.AddPostgreSqlUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkManager)).Should().Be(1);
        services.Single(d => d.ServiceType == typeof(IUnitOfWorkManager)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void should_refuse_root_resolution_when_scopes_are_validated()
    {
        using var provider = new ServiceCollection()
            .AddPostgreSqlUnitOfWork()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => provider.GetRequiredService<IUnitOfWorkManager>();

        act.Should()
            .Throw<InvalidOperationException>("a scoped manager resolved from the root is a captive dependency");
    }

    [Fact]
    public async Task should_validate_arguments_before_touching_the_connection()
    {
        await using var provider = new ServiceCollection().AddPostgreSqlUnitOfWork().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
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
        manager.Current.Should().BeNull("a rejected call must not claim the scope's slot");
    }
}
