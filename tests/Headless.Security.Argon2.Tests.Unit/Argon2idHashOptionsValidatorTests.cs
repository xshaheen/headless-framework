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
    [MemberData(nameof(InvalidOptions))]
    public void should_reject_out_of_bounds_values(string property, Action<Argon2idHashOptions> mutate)
    {
        // given
        var options = new Argon2idHashOptions();
        mutate(options);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    public static TheoryData<string, Action<Argon2idHashOptions>> InvalidOptions =>
        new()
        {
            { "MemorySize", o => o.MemorySize = SecretHashLimits.MinArgon2idMemorySize - 1 },
            { "MemorySize", o => o.MemorySize = SecretHashLimits.MaxArgon2idMemorySize + 1 },
            { "Iterations", o => o.Iterations = 0 },
            { "Iterations", o => o.Iterations = SecretHashLimits.MaxArgon2idIterations + 1 },
            { "HashSize", o => o.HashSize = 15 },
            { "HashSize", o => o.HashSize = 65 },
        };
}
