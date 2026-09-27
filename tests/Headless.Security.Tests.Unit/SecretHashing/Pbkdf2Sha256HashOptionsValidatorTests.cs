// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

namespace Tests.SecretHashing;

public sealed class Pbkdf2Sha256HashOptionsValidatorTests
{
    private readonly Pbkdf2Sha256HashOptionsValidator _sut = new();

    [Fact]
    public void should_accept_the_defaults()
    {
        _sut.Validate(new Pbkdf2Sha256HashOptions()).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void should_reject_out_of_bounds_values(string property, Action<Pbkdf2Sha256HashOptions> mutate)
    {
        // given
        var options = new Pbkdf2Sha256HashOptions();
        mutate(options);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    public static TheoryData<string, Action<Pbkdf2Sha256HashOptions>> InvalidOptions =>
        new()
        {
            { "Iterations", o => o.Iterations = 0 },
            { "Iterations", o => o.Iterations = SecretHashLimits.MaxPbkdf2Iterations + 1 },
            { "SaltSize", o => o.SaltSize = 15 },
            { "SaltSize", o => o.SaltSize = 65 },
            { "HashSize", o => o.HashSize = 15 },
            { "HashSize", o => o.HashSize = 65 },
        };
}
