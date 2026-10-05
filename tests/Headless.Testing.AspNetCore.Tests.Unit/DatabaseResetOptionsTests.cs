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
    public void should_default_to_empty_tables_to_preserve()
    {
        var options = new DatabaseResetOptions();

        options.TablesToPreserve.Should().BeEmpty();
    }

    [Fact]
    public void should_default_to_null_connection_provider()
    {
        var options = new DatabaseResetOptions();

        options.ConnectionProvider.Should().BeNull();
    }

    [Fact]
    public void should_default_to_null_additional_transient_exception_filter()
    {
        var options = new DatabaseResetOptions();

        options.AdditionalTransientExceptionFilter.Should().BeNull();
    }

    [Fact]
    public void should_accept_custom_tables_to_preserve()
    {
        var options = new DatabaseResetOptions
        {
            TablesToPreserve = [new Table("CustomTable"), new Table("AnotherTable")],
        };

        options.TablesToPreserve.Should().HaveCount(2);
    }
}
