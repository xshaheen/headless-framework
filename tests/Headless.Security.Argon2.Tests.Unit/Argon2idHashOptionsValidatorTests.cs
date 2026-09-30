// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;
using Headless.Testing.Tests;

namespace Tests;

public sealed class Argon2idHashOptionsValidatorTests : TestBase
{
    private readonly Argon2idHashOptionsValidator _sut = new();

    [Fact]
    public void should_accept_the_defaults()
    {
        _sut.Validate(new Argon2idHashOptions()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(Argon2idHashOptions.MemorySize), SecretHashLimits.MinArgon2idMemorySize - 1)]
    [InlineData(nameof(Argon2idHashOptions.MemorySize), SecretHashLimits.MaxArgon2idMemorySize + 1)]
    [InlineData(nameof(Argon2idHashOptions.Iterations), 0)]
    [InlineData(nameof(Argon2idHashOptions.Iterations), SecretHashLimits.MaxArgon2idIterations + 1)]
    [InlineData(nameof(Argon2idHashOptions.HashSize), 15)]
    [InlineData(nameof(Argon2idHashOptions.HashSize), 65)]
    public void should_reject_out_of_bounds_values(string property, int value)
    {
        // given
        var options = new Argon2idHashOptions();
        _Assign(options, property, value);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    private static void _Assign(Argon2idHashOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(Argon2idHashOptions.MemorySize):
                options.MemorySize = value;
                break;
            case nameof(Argon2idHashOptions.Iterations):
                options.Iterations = value;
                break;
            case nameof(Argon2idHashOptions.HashSize):
                options.HashSize = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }
}
