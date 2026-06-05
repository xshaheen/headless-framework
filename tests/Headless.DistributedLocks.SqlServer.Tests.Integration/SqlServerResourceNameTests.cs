// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.SqlServer;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlServerResourceNameTests : TestBase
{
    [Fact]
    public void should_return_resource_unchanged_when_it_fits_sql_server_limit()
    {
        var resource = new string('a', SqlServerDistributedLockFieldLimits.MaxResourceNameLength);

        SqlServerResourceName.Encode(resource).Should().Be(resource);
    }

    [Fact]
    public void should_hash_resource_when_it_exceeds_sql_server_limit()
    {
        var resource = new string('a', SqlServerDistributedLockFieldLimits.MaxResourceNameLength + 1);

        var encoded = SqlServerResourceName.Encode(resource);

        encoded.Should().StartWith("sha256:");
        encoded.Should().HaveLength("sha256:".Length + 64);
        encoded.Should().Be(SqlServerResourceName.Encode(resource));
    }

    [Fact]
    public void should_encode_distinct_long_resources_to_distinct_hashes()
    {
        var first = new string('a', SqlServerDistributedLockFieldLimits.MaxResourceNameLength + 1);
        var second = new string('b', SqlServerDistributedLockFieldLimits.MaxResourceNameLength + 1);

        SqlServerResourceName.Encode(first).Should().NotBe(SqlServerResourceName.Encode(second));
    }
}
