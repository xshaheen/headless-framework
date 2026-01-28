// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class NotBeDefaultTests
{
    [Fact]
    public void should_throw_argument_exception_when_pass_default_value()
    {
        const int zero = 0;
        Action act1 = () => Argument.IsNotDefault(zero);
        act1.Should().Throw<ArgumentException>().WithParameterName(nameof(zero));

        int? nullableZero = 0;
        Action act2 = () => Argument.IsNotDefault(nullableZero);
        act2.Should().Throw<ArgumentException>().WithParameterName(nameof(nullableZero));

        var emptyGuid = Guid.Empty;
        Action act3 = () => Argument.IsNotDefault(emptyGuid);
        act3.Should().Throw<ArgumentException>().WithParameterName(nameof(emptyGuid));

        var defaultDateTime = default(DateTime);
        Action act4 = () => Argument.IsNotDefault(defaultDateTime);
        act4.Should().Throw<ArgumentException>().WithParameterName(nameof(defaultDateTime));
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
