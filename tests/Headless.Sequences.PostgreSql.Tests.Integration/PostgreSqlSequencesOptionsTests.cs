// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences.PostgreSql;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>Option defaults that need no database, so the class stays outside the container collection.</summary>
public sealed class PostgreSqlSequencesOptionsTests : TestBase
{
    [Fact]
    public void should_default_the_schema_to_the_shared_headless_schema()
    {
        new PostgreSqlSequencesOptions().Schema.Should().Be("headless");
    }
}
