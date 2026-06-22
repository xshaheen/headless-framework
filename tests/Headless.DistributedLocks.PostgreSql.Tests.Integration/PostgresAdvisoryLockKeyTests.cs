// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PostgresAdvisoryLockKeyTests : TestBase
{
    [Fact]
    public void should_round_trip_bigint_key()
    {
        var key = new PostgresAdvisoryLockKey(42L);

        var roundTripped = PostgresAdvisoryLockKey.FromString(key.ToString(), allowHashing: false);

        roundTripped.Should().Be(key);
        roundTripped.HasSingleKey.Should().BeTrue();
    }

    [Fact]
    public void should_round_trip_int_pair_key()
    {
        var key = new PostgresAdvisoryLockKey(10, 20);

        var roundTripped = PostgresAdvisoryLockKey.FromString(key.ToString(), allowHashing: false);

        roundTripped.Should().Be(key);
        roundTripped.HasSingleKey.Should().BeFalse();
    }

    [Fact]
    public void should_hash_long_names_when_allowed()
    {
        var first = PostgresAdvisoryLockKey.FromString(new string('a', 40));
        var second = PostgresAdvisoryLockKey.FromString(new string('a', 40));

        first.Should().Be(second);
        first.HasSingleKey.Should().BeTrue();
    }

    [Fact]
    public void should_throw_when_key_getter_called_on_pair_key()
    {
        var key = new PostgresAdvisoryLockKey(10, 20);

        var act = () => key.Key;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_format_exception_when_long_name_cannot_be_encoded_without_hashing()
    {
        var act = () => PostgresAdvisoryLockKey.FromString(new string('a', 40), allowHashing: false);

        act.Should().Throw<FormatException>();
    }
}
