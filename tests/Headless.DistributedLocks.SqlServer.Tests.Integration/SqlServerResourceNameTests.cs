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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_invalid_resource_names(string resource)
    {
        var act = () => SqlServerResourceName.Encode(resource);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_be_case_sensitive_and_not_collide_on_different_case()
    {
        // Short resources
        SqlServerResourceName.Encode("abc").Should().NotBe(SqlServerResourceName.Encode("ABC"));

        // Long resources over the limit
        var prefix = new string('a', SqlServerDistributedLockFieldLimits.MaxResourceNameLength - 5);
        var first = prefix + "xyz1";
        var second = prefix + "XYZ1";

        SqlServerResourceName.Encode(first).Should().NotBe(SqlServerResourceName.Encode(second));
    }

    [Fact]
    public void should_encode_special_characters_correctly()
    {
        var specialResource = "lock:resource-with-special_chars#$@!";
        SqlServerResourceName.Encode(specialResource).Should().Be(specialResource);

        var longSpecialResource =
            new string('a', SqlServerDistributedLockFieldLimits.MaxResourceNameLength)
            + "lock:resource-with-special_chars#$@!";
        var encoded = SqlServerResourceName.Encode(longSpecialResource);
        encoded.Should().StartWith("sha256:");
        encoded.Should().HaveLength("sha256:".Length + 64);
    }

    [Fact]
    public void should_quote_valid_sql_identifiers()
    {
        SqlServerIdentifier.Quote("my_table").Should().Be("[my_table]");
        SqlServerIdentifier.Quote("table]name").Should().Be("[table]]name]");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_invalid_identifiers(string identifier)
    {
        var act = () => SqlServerIdentifier.Quote(identifier);
        act.Should().Throw<ArgumentException>();

        var actSeq = () => SqlServerIdentifier.FenceSequenceName(identifier);
        actSeq.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_generate_safe_fence_sequence_names()
    {
        // Under maximum identifier length
        SqlServerIdentifier.FenceSequenceName("my-prefix").Should().Be("headless_distlocks_fence_my_prefix");

        // Long prefix exceeding limits should be truncated safely
        var longPrefix = new string('a', 200);
        var sequenceName = SqlServerIdentifier.FenceSequenceName(longPrefix);
        sequenceName.Length.Should().BeLessThanOrEqualTo(SqlServerDistributedLockFieldLimits.MaxIdentifierLength);
        sequenceName.Should().NotEndWith("_");
    }
}
