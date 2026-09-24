// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;

namespace Tests.SecretHashing;

public sealed class SecretHasherOptionsValidatorTests
{
    private readonly SecretHasherOptionsValidator _sut = new();

    [Fact]
    public void should_accept_the_defaults()
    {
        _sut.Validate(new SecretHasherOptions()).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void should_reject_out_of_bounds_values(string property, Action<SecretHasherOptions> mutate)
    {
        // given
        var options = new SecretHasherOptions();
        mutate(options);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    public static TheoryData<string, Action<SecretHasherOptions>> InvalidOptions =>
        new()
        {
            { "Algorithm", o => o.Algorithm = "" },
            { "MaxSecretLength", o => o.MaxSecretLength = 0 },
            { "MaxSecretLength", o => o.MaxSecretLength = SecretHashLimits.MaxSecretLength + 1 },
            { "Argon2id.MemorySize", o => o.Argon2id.MemorySize = 7 },
            { "Argon2id.MemorySize", o => o.Argon2id.MemorySize = SecretHashLimits.MaxArgon2idMemorySize + 1 },
            { "Argon2id.Iterations", o => o.Argon2id.Iterations = 0 },
            { "Argon2id.Iterations", o => o.Argon2id.Iterations = 17 },
            { "Argon2id.HashSize", o => o.Argon2id.HashSize = 15 },
            { "Argon2id.HashSize", o => o.Argon2id.HashSize = 65 },
            { "Pbkdf2Sha256.Iterations", o => o.Pbkdf2Sha256.Iterations = 0 },
            { "Pbkdf2Sha256.Iterations", o => o.Pbkdf2Sha256.Iterations = SecretHashLimits.MaxPbkdf2Iterations + 1 },
            { "Pbkdf2Sha256.SaltSize", o => o.Pbkdf2Sha256.SaltSize = 15 },
            { "Pbkdf2Sha256.HashSize", o => o.Pbkdf2Sha256.HashSize = 65 },
            { "CostCheck.Mode", o => o.CostCheck.Mode = (SecretHasherCostCheckMode)42 },
            { "CostCheck.MinimumDuration", o => o.CostCheck.MinimumDuration = TimeSpan.FromMilliseconds(-1) },
            { "CostCheck.MaximumDuration", o => o.CostCheck.MaximumDuration = o.CostCheck.MinimumDuration },
        };
}
