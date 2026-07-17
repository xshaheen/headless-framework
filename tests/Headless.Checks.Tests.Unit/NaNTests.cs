// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class NaNTests
{
    [Fact]
    public void should_throw_argument_exception_when_is_na_n_pass_not_na_n_value()
    {
        const double zero = 0;
        Action actZero = () => Argument.IsNaN(zero);
        actZero.Should().Throw<ArgumentException>().WithParameterName(nameof(zero));

        var positive = Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1);
        Action actPositive = () => Argument.IsNaN(positive);
        actPositive.Should().Throw<ArgumentException>().WithParameterName(nameof(positive));

        var negative = -1 * (Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1));
        Action actNegative = () => Argument.IsNaN(negative);
        actNegative.Should().Throw<ArgumentException>().WithParameterName(nameof(negative));
    }

    [Fact]
    public void should_not_throw_argument_exception_when_is_na_n_pass_na_n_value()
    {
        const double doubleNaN = double.NaN;
        Action actDoubleNaN = () => Argument.IsNaN(doubleNaN);
        actDoubleNaN.Should().NotThrow();

        const float floatNaN = float.NaN;
        Action actFloatNaN = () => Argument.IsNaN(floatNaN);
        actFloatNaN.Should().NotThrow();
    }

    [Fact]
    public void should_throw_argument_exception_when_is_not_na_n_pass_na_n_value()
    {
        const double doubleNaN = double.NaN;
        Action actDoubleNaN = () => Argument.IsNotNaN(doubleNaN);
        actDoubleNaN.Should().Throw<ArgumentException>().WithParameterName(nameof(doubleNaN));

        const float floatNaN = float.NaN;
        Action actFloatNaN = () => Argument.IsNotNaN(floatNaN);
        actFloatNaN.Should().Throw<ArgumentException>().WithParameterName(nameof(floatNaN));
    }

    [Fact]
    public void should_not_throw_argument_exception_when_is_not_na_n_pass_not_na_n_value()
    {
        const double zero = 0;
        Action actZero = () => Argument.IsNotNaN(zero);
        actZero.Should().NotThrow();

        var positive = Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1);
        Action actPositive = () => Argument.IsNotNaN(positive);
        actPositive.Should().NotThrow();

        var negative = -1 * (Random.Shared.NextDouble() + Random.Shared.Next(1, int.MaxValue - 1));
        Action actNegative = () => Argument.IsNotNaN(negative);
        actNegative.Should().NotThrow();
    }
}
