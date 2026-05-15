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

    #endregion
}
