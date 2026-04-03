// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.AspNetCore;

namespace Tests;

public sealed class DatabaseResetTests
{
    [Fact]
    public async Task should_throw_when_connection_not_open_on_create()
    {
        var connection = Substitute.For<DbConnection>();
        connection.State.Returns(ConnectionState.Closed);

        Func<Task> act = async () => await DatabaseReset.CreateAsync(connection);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*open*");
    }
}
