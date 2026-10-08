// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class UnitOfWorkFactorySqliteExtensionsTests : TestBase
{
    [Fact]
    public async Task should_validate_arguments_before_touching_the_connection()
    {
        await using var provider = new ServiceCollection().AddUnitOfWork().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqliteConnection("Data Source=unused.db");

        var beginAct = () => manager.BeginAsync((SqliteConnection)null!, AbortToken).AsTask();
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
