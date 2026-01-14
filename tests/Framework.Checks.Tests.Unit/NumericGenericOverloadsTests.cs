// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Numerics;
using Framework.Checks;

namespace Tests;

public class NumericGenericOverloadsTests
{
    // IsPositive tests for generic INumber<T> overloads

    [Fact]
    public void is_positive_byte_with_positive_value_does_not_throw()
    {
        // given
        byte value = 5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_byte_with_zero_throws()
    {
        // given
        byte argument = 0;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_sbyte_with_positive_value_does_not_throw()
    {
        // given
        sbyte value = 5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_sbyte_with_negative_value_throws()
    {
        // given
        sbyte argument = -5;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_ushort_with_positive_value_does_not_throw()
    {
        // given
        ushort value = 5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_ushort_with_zero_throws()
    {
        // given
        ushort argument = 0;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_uint_with_positive_value_does_not_throw()
    {
        // given
        uint value = 5u;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_uint_with_zero_throws()
    {
        // given
        uint argument = 0u;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_ulong_with_positive_value_does_not_throw()
    {
        // given
        ulong value = 5ul;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_ulong_with_zero_throws()
    {
        // given
        ulong argument = 0ul;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_nint_with_positive_value_does_not_throw()
    {
        // given
        nint value = 5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_nint_with_negative_value_throws()
    {
        // given
        nint argument = -5;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_nuint_with_positive_value_does_not_throw()
    {
        // given
        nuint value = 5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_nuint_with_zero_throws()
    {
        // given
        nuint argument = 0;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_big_integer_with_positive_value_does_not_throw()
    {
        // given
        BigInteger value = new(12345);

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_big_integer_with_negative_value_throws()
    {
        // given
        BigInteger argument = new(-12345);

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_half_with_positive_value_does_not_throw()
    {
        // given
        Half value = (Half)5.5;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_half_with_negative_value_throws()
    {
        // given
        Half argument = (Half)(-5.5);

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_int128_with_positive_value_does_not_throw()
    {
        // given
        Int128 value = 123;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_int128_with_negative_value_throws()
    {
        // given
        Int128 argument = -123;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_uint128_with_positive_value_does_not_throw()
    {
        // given
        UInt128 value = 123;

        // when & then
        Argument.IsPositive(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_uint128_with_zero_throws()
    {
        // given
        UInt128 argument = UInt128.Zero;

        // when
        Action action = () => Argument.IsPositive(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsNegative tests for generic INumber<T> overloads

    [Fact]
    public void is_negative_sbyte_with_negative_value_does_not_throw()
    {
        // given
        sbyte value = -5;

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_sbyte_with_positive_value_throws()
    {
        // given
        sbyte argument = 5;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_nint_with_negative_value_does_not_throw()
    {
        // given
        nint value = -5;

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_nint_with_positive_value_throws()
    {
        // given
        nint argument = 5;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_big_integer_with_negative_value_does_not_throw()
    {
        // given
        BigInteger value = new(-12345);

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_big_integer_with_positive_value_throws()
    {
        // given
        BigInteger argument = new(12345);

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_half_with_negative_value_does_not_throw()
    {
        // given
        Half value = (Half)(-5.5);

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_half_with_positive_value_throws()
    {
        // given
        Half argument = (Half)5.5;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_int128_with_negative_value_does_not_throw()
    {
        // given
        Int128 value = -123;

        // when & then
        Argument.IsNegative(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_int128_with_positive_value_throws()
    {
        // given
        Int128 argument = 123;

        // when
        Action action = () => Argument.IsNegative(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // IsPositiveOrZero tests for generic INumber<T> overloads

    [Fact]
    public void is_positive_or_zero_byte_with_zero_does_not_throw()
    {
        // given
        byte value = 0;

        // when & then
        Argument.IsPositiveOrZero(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_or_zero_uint_with_positive_value_does_not_throw()
    {
        // given
        uint value = 5u;

        // when & then
        Argument.IsPositiveOrZero(value).Should().Be(value);
    }

    [Fact]
    public void is_positive_or_zero_nint_with_negative_value_throws()
    {
        // given
        nint argument = -5;

        // when
        Action action = () => Argument.IsPositiveOrZero(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_positive_or_zero_half_with_zero_does_not_throw()
    {
        // given
        Half value = Half.Zero;

        // when & then
        Argument.IsPositiveOrZero(value).Should().Be(value);
    }

    // IsNegativeOrZero tests for generic INumber<T> overloads

    [Fact]
    public void is_negative_or_zero_sbyte_with_zero_does_not_throw()
    {
        // given
        sbyte value = 0;

        // when & then
        Argument.IsNegativeOrZero(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_or_zero_nint_with_negative_value_does_not_throw()
    {
        // given
        nint value = -5;

        // when & then
        Argument.IsNegativeOrZero(value).Should().Be(value);
    }

    [Fact]
    public void is_negative_or_zero_nint_with_positive_value_throws()
    {
        // given
        nint argument = 5;

        // when
        Action action = () => Argument.IsNegativeOrZero(argument);

        // then
        action.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void is_negative_or_zero_half_with_zero_does_not_throw()
    {
        // given
        Half value = Half.Zero;

        // when & then
        Argument.IsNegativeOrZero(value).Should().Be(value);
    }
}
