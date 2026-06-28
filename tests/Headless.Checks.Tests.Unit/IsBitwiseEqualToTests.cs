// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class IsBitwiseEqualToTests
{
    [Fact]
    public void is_bitwise_equal_to_should_return_value_for_equal_primitives()
    {
        Argument.IsBitwiseEqualTo((byte)7, (byte)7).Should().Be(7);
        Argument.IsBitwiseEqualTo((short)-3, (short)-3).Should().Be(-3);
        Argument.IsBitwiseEqualTo(42, 42).Should().Be(42);
        Argument.IsBitwiseEqualTo(42L, 42L).Should().Be(42L);
        Argument.IsBitwiseEqualTo(1.5f, 1.5f).Should().Be(1.5f);
        Argument.IsBitwiseEqualTo(1.5d, 1.5d).Should().Be(1.5d);
    }

    [Fact]
    public void is_bitwise_equal_to_should_throw_when_bytes_differ()
    {
        var value = 42;
        var action = () => Argument.IsBitwiseEqualTo(value, 43);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("The argument \"value\" must be bitwise-equal to <43>. (Parameter 'value')");
    }

    [Fact]
    public void is_bitwise_equal_to_should_distinguish_positive_and_negative_zero()
    {
        // 0.0f == -0.0f is true, but their bit patterns differ.
        var action = () => Argument.IsBitwiseEqualTo(0.0f, -0.0f);
        action.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void is_bitwise_equal_to_should_treat_identical_nan_payloads_as_equal()
    {
        // NaN != NaN by value, but identical bit payloads are bitwise-equal.
        Argument.IsBitwiseEqualTo(float.NaN, float.NaN).Should().Be(float.NaN);
    }

    [Fact]
    public void is_bitwise_equal_to_should_handle_large_unmanaged_types()
    {
        var id = Guid.NewGuid();

        Argument.IsBitwiseEqualTo(id, id).Should().Be(id);

        var action = () => Argument.IsBitwiseEqualTo(id, Guid.NewGuid());
        action.Should().ThrowExactly<ArgumentException>();
    }
}
