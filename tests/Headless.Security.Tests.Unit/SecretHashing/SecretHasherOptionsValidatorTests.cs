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
    [InlineData(InvalidCase.MaxSecretLengthZero)]
    [InlineData(InvalidCase.MaxSecretLengthAboveLimit)]
    [InlineData(InvalidCase.CostCheckModeUndefined)]
    [InlineData(InvalidCase.MinimumDurationNegative)]
    [InlineData(InvalidCase.MaximumDurationNotAboveMinimum)]
    public void should_reject_out_of_bounds_values(InvalidCase invalidCase)
    {
        // given
        var options = new SecretHasherOptions();
        var property = _Apply(options, invalidCase);

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == property);
    }

    // Serializable enum discriminator (xUnit1044) instead of a TheoryData of mutation delegates; each case is
    // applied inside the test so Test Explorer can enumerate the rows.
    public enum InvalidCase
    {
        MaxSecretLengthZero,
        MaxSecretLengthAboveLimit,
        CostCheckModeUndefined,
        MinimumDurationNegative,
        MaximumDurationNotAboveMinimum,
    }

    /// <summary>Applies the invalid value and returns the property the validator must report.</summary>
    private static string _Apply(SecretHasherOptions options, InvalidCase invalidCase)
    {
        switch (invalidCase)
        {
            case InvalidCase.MaxSecretLengthZero:
                options.MaxSecretLength = 0;
                return "MaxSecretLength";
            case InvalidCase.MaxSecretLengthAboveLimit:
                options.MaxSecretLength = SecretHashLimits.MaxSecretLength + 1;
                return "MaxSecretLength";
            case InvalidCase.CostCheckModeUndefined:
                options.CostCheck.Mode = (SecretHasherCostCheckMode)42;
                return "CostCheck.Mode";
            case InvalidCase.MinimumDurationNegative:
                options.CostCheck.MinimumDuration = TimeSpan.FromMilliseconds(-1);
                return "CostCheck.MinimumDuration";
            case InvalidCase.MaximumDurationNotAboveMinimum:
                options.CostCheck.MaximumDuration = options.CostCheck.MinimumDuration;
                return "CostCheck.MaximumDuration";
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }
    }
}
