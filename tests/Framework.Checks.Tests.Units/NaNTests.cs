// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class NaNTests
{
    [Fact]
    public void IsNaN_should_throw_argument_exception_when_pass_not_NaN_value()
    {
        const double zero = 0;
        Assert.Throws<ArgumentException>(nameof(zero), () => Argument.IsNaN(zero));

        var positive = Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1);
        Assert.Throws<ArgumentException>(nameof(positive), () => Argument.IsNaN(positive));

        var negative = -1 * (Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1));
        Assert.Throws<ArgumentException>(nameof(negative), () => Argument.IsNaN(negative));
    }

    [Fact]
    public void IsNaN_should_not_throw_argument_exception_when_pass_NaN_value()
    {
        const double doubleNaN = double.NaN;
        Argument.IsNaN(doubleNaN);

        const float floatNaN = float.NaN;
        Argument.IsNaN(floatNaN);
    }

    [Fact]
    public void IsNotNaN_should_throw_argument_exception_when_pass_NaN_value()
    {
        const double doubleNaN = double.NaN;
        Assert.Throws<ArgumentException>(nameof(doubleNaN), () => Argument.IsNotNaN(doubleNaN));

        const float floatNaN = float.NaN;
        Assert.Throws<ArgumentException>(nameof(floatNaN), () => Argument.IsNotNaN(floatNaN));
    }

    [Fact]
    public void IsNotNaN_should_not_throw_argument_exception_when_pass_not_NaN_value()
    {
        const double zero = 0;
        Argument.IsNotNaN(zero);

        var positive = Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1);
        Argument.IsNotNaN(positive);

        var negative = -1 * (Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1));
        Argument.IsNotNaN(negative);
    }
}
