// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.AspNetCore;
using Respawn;
using Respawn.Graph;

namespace Tests;

public sealed class DatabaseResetOptionsTests
{
    [Fact]
    public void should_default_to_postgres_adapter()
    {
        var options = new DatabaseResetOptions();

        options.DbAdapter.Should().Be(DbAdapter.Postgres);
    }

    [Fact]
    public void should_default_to_empty_tables_to_ignore()
    {
        var options = new DatabaseResetOptions();

        options.TablesToIgnore.Should().BeEmpty();
    }

    [Fact]
    public void should_default_to_null_connection_provider()
    {
        var options = new DatabaseResetOptions();

        options.ConnectionProvider.Should().BeNull();
    }

    [Fact]
    public void should_accept_custom_tables_to_ignore()
    {
        var options = new DatabaseResetOptions
        {
            TablesToIgnore = [new Table("CustomTable"), new Table("AnotherTable")],
        };

        options.TablesToIgnore.Should().HaveCount(2);
    }
}
