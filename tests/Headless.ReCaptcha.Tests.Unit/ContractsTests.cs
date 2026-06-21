// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;

namespace Tests;

public sealed class ReCaptchaErrorCodesExtensionsTests
{
    [Theory]
    [InlineData("missing-input-secret", ReCaptchaError.MissingInputSecret)]
    [InlineData("invalid-input-secret", ReCaptchaError.InvalidInputSecret)]
    [InlineData("missing-input-response", ReCaptchaError.MissingInputResponse)]
    [InlineData("invalid-input-response", ReCaptchaError.InvalidInputResponse)]
    [InlineData("bad-request", ReCaptchaError.BadRequest)]
    [InlineData("timeout-or-duplicate", ReCaptchaError.TimeOutOrDuplicate)]
    public void should_map_known_codes(string code, ReCaptchaError expected)
    {
        new[] { code }.ToReCaptchaErrors().Should().ContainSingle().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("totally-unknown")]
    [InlineData("Bad-Request")] // case-sensitive: capitalized variant is not recognized
    [InlineData("")]
    public void should_map_unrecognized_codes_to_unknown(string code)
    {
        new[] { code }.ToReCaptchaErrors().Should().ContainSingle().Which.Should().Be(ReCaptchaError.Unknown);
    }

    [Fact]
    public void should_return_empty_for_null()
    {
        ((string[]?)null).ToReCaptchaErrors().Should().BeEmpty();
    }

    [Fact]
    public void should_map_each_element_preserving_order()
    {
        new[] { "bad-request", "nope", "timeout-or-duplicate" }
            .ToReCaptchaErrors()
            .Should()
            .Equal(ReCaptchaError.BadRequest, ReCaptchaError.Unknown, ReCaptchaError.TimeOutOrDuplicate);
    }
}

public sealed class ReCaptchaOptionsValidatorTests
{
    private readonly ReCaptchaOptionsValidator _validator = new();

    [Fact]
    public void should_pass_for_valid_options()
    {
        _validator.Validate(TestHelpers.Options()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_site_key_is_empty()
    {
        var options = TestHelpers.Options(siteKey: "");

        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_site_secret_is_empty()
    {
        var options = TestHelpers.Options(siteSecret: "");

        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_verify_base_url_is_not_an_http_url()
    {
        var options = TestHelpers.Options();
        options.VerifyBaseUrl = "not-a-url";

        _validator.Validate(options).IsValid.Should().BeFalse();
    }
}
