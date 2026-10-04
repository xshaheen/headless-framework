// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

/// <summary>
/// Covers what is left of the SQL-parameter binding helper.
/// </summary>
/// <remarks>
/// This suite used to exercise a matrix of <c>DateTimeKind</c> normalization rules — Utc passed through, Local
/// converted, and (the subtle one) Unspecified STAMPED as UTC rather than converted, because converting would
/// shift it by the host's offset. Persisted instants are <see cref="DateTimeOffset"/> now, so an instant cannot
/// arrive ambiguous and none of those rules have anything left to decide. That whole class of bug is gone by
/// construction; only null-handling remains.
/// </remarks>
public sealed class DateTimeUtcExtensionsTests : TestBase
{
    [Fact]
    public void should_map_null_to_dbnull()
    {
        // given
        DateTimeOffset? value = null;

        // when
        var result = value.ToUtcParameterValue();

        // then
        result.Should().Be(DBNull.Value);
    }

    [Fact]
    public void should_bind_an_instant_normalized_to_utc()
    {
        // given — a non-zero offset, so a helper that bound the wall-clock reading rather than the instant is caught.
        DateTimeOffset? value = new DateTimeOffset(2025, 6, 15, 13, 30, 45, TimeSpan.FromHours(3));

        // when
        var result = value.ToUtcParameterValue();

        // then — the same instant, expressed at offset zero.
        result.Should().BeOfType<DateTimeOffset>();
        ((DateTimeOffset)result).Offset.Should().Be(TimeSpan.Zero);
        ((DateTimeOffset)result).Should().Be(value.Value);
    }

    [Fact]
    public void should_bind_an_already_utc_instant_unchanged()
    {
        // given
        DateTimeOffset? value = new DateTimeOffset(2025, 6, 15, 10, 30, 45, TimeSpan.Zero);

        // when
        var result = value.ToUtcParameterValue();

        // then
        ((DateTimeOffset)result)
            .Should()
            .Be(value.Value);
    }
}
