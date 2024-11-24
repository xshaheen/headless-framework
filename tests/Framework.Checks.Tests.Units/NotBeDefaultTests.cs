// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class NotBeDefaultTests
{
    [Fact]
    public void should_throw_argument_exception_when_pass_default_value()
    {
        var zero = 0;
        Assert.Throws<ArgumentException>(nameof(zero), () => Argument.IsNotDefault(zero));
        int? nullableZero = 0;
        Assert.Throws<ArgumentException>(nameof(nullableZero), () => Argument.IsNotDefault(nullableZero));
        var emptyGuid = Guid.Empty;
        Assert.Throws<ArgumentException>(nameof(emptyGuid), () => Argument.IsNotDefault(emptyGuid));
        var defaultDateTime = default(DateTime);
        Assert.Throws<ArgumentException>(nameof(defaultDateTime), () => Argument.IsNotDefault(defaultDateTime));
    }

    [Fact]
    public void should_do_nothing_given_non_default_value_and_return_expected_value()
    {
        Argument.IsNotDefault(1).Should().Be(1);
        Argument.IsNotDefault((int?)-1).Should().Be((int?)-1);
        Argument.IsNotDefault((int?)1).Should().Be((int?)1);
        Argument.IsNotDefault((int?)null).Should().Be(null);
        Argument.IsNotDefault(new DateTime(2000, 1, 1)).Should().Be(new DateTime(2000, 1, 1));
        var guid = Guid.NewGuid();
        Argument.IsNotDefault(guid).Should().Be(guid);
    }
}
