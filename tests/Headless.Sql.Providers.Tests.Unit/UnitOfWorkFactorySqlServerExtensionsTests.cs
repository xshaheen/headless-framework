// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class UnitOfWorkFactorySqlServerExtensionsTests : TestBase
{
    [Fact]
    public async Task should_validate_arguments_before_touching_the_connection()
    {
        await using var provider = new ServiceCollection().AddUnitOfWork().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        await using var connection = new SqlConnection(
            "Server=localhost;Database=unused;Integrated Security=false;User Id=x;Password=y;TrustServerCertificate=true"
        );

        var beginAct = () => manager.BeginAsync((SqlConnection)null!, AbortToken).AsTask();
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
