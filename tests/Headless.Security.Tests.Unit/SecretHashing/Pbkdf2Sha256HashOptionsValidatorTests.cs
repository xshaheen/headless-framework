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
    [InlineData(nameof(Pbkdf2Sha256HashOptions.Iterations), 0)]
    [InlineData(nameof(Pbkdf2Sha256HashOptions.Iterations), SecretHashLimits.MaxPbkdf2Iterations + 1)]
    [InlineData(nameof(Pbkdf2Sha256HashOptions.SaltSize), 15)]
    [InlineData(nameof(Pbkdf2Sha256HashOptions.SaltSize), 65)]
    [InlineData(nameof(Pbkdf2Sha256HashOptions.HashSize), 15)]
    [InlineData(nameof(Pbkdf2Sha256HashOptions.HashSize), 65)]
    public void should_reject_out_of_bounds_values(string property, int value)
    {
        // given
        var options = new Pbkdf2Sha256HashOptions();
        _Assign(options, property, value);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    private static void _Assign(Pbkdf2Sha256HashOptions options, string property, int value)
    {
        switch (property)
        {
            case nameof(Pbkdf2Sha256HashOptions.Iterations):
                options.Iterations = value;
                break;
            case nameof(Pbkdf2Sha256HashOptions.SaltSize):
                options.SaltSize = value;
                break;
            case nameof(Pbkdf2Sha256HashOptions.HashSize):
                options.HashSize = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }
}
