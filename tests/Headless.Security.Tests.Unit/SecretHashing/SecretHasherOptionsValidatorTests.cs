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
            { "MaxSecretLength", o => o.MaxSecretLength = 0 },
            { "MaxSecretLength", o => o.MaxSecretLength = SecretHashLimits.MaxSecretLength + 1 },
            { "CostCheck.Mode", o => o.CostCheck.Mode = (SecretHasherCostCheckMode)42 },
            { "CostCheck.MinimumDuration", o => o.CostCheck.MinimumDuration = TimeSpan.FromMilliseconds(-1) },
            { "CostCheck.MaximumDuration", o => o.CostCheck.MaximumDuration = o.CostCheck.MinimumDuration },
        };
}
