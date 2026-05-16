// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class DateTimeUtcExtensionsTests : TestBase
{
    #region ToUtcParameterValue

    [Fact]
    public void to_utc_parameter_value_should_return_dbnull_when_value_is_null()
    {
        DateTime? value = null;

        var result = value.ToUtcParameterValue();

        result.Should().Be(DBNull.Value);
    }

    [Fact]
    public void to_utc_parameter_value_should_return_value_unchanged_when_already_utc()
    {
        var utc = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        DateTime? value = utc;

        var result = (DateTime)value.ToUtcParameterValue();

        result.Should().Be(utc);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void to_utc_parameter_value_should_convert_local_to_utc()
    {
        var local = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Local);
        DateTime? value = local;

        var result = (DateTime)value.ToUtcParameterValue();

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void to_utc_parameter_value_should_tag_unspecified_as_utc_without_shifting_value()
    {
        // Unspecified must NOT be routed through ToUniversalTime — that would silently shift by
        // the local-clock offset. SpecifyKind preserves the wall-clock components.
        var unspecified = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime? value = unspecified;

        var result = (DateTime)value.ToUtcParameterValue();

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Year.Should().Be(unspecified.Year);
        result.Month.Should().Be(unspecified.Month);
        result.Day.Should().Be(unspecified.Day);
        result.Hour.Should().Be(unspecified.Hour);
        result.Minute.Should().Be(unspecified.Minute);
        result.Second.Should().Be(unspecified.Second);
    }

    #endregion

    #region ToUtcOrSelf

    [Fact]
    public void to_utc_or_self_should_return_null_when_value_is_null()
    {
        DateTime? value = null;

        var result = value.ToUtcOrSelf();

        result.Should().BeNull();
    }

    [Fact]
    public void to_utc_or_self_should_return_value_unchanged_when_already_utc()
    {
        var utc = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        DateTime? value = utc;

        var result = value.ToUtcOrSelf();

        result.Should().Be(utc);
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void to_utc_or_self_should_convert_local_to_utc()
    {
        var local = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Local);
        DateTime? value = local;

        var result = value.ToUtcOrSelf();

        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void to_utc_or_self_should_tag_unspecified_as_utc_without_shifting_value()
    {
        // Unspecified must NOT be routed through ToUniversalTime — that would silently shift by
        // the local-clock offset. SpecifyKind preserves the wall-clock components.
        var unspecified = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Unspecified);
        DateTime? value = unspecified;

        var result = value.ToUtcOrSelf();

        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Year.Should().Be(unspecified.Year);
        result.Value.Month.Should().Be(unspecified.Month);
        result.Value.Day.Should().Be(unspecified.Day);
        result.Value.Hour.Should().Be(unspecified.Hour);
        result.Value.Minute.Should().Be(unspecified.Minute);
        result.Value.Second.Should().Be(unspecified.Second);
    }

    #endregion
}
