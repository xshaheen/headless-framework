// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;

namespace Tests;

/// <summary>Maps Google's raw <c>error-codes</c> strings to the <see cref="ReCaptchaError"/> enum.</summary>
public sealed class ReCaptchaErrorCodesExtensionsTests
{
    [Theory]
    [InlineData("missing-input-secret", ReCaptchaError.MissingInputSecret)]
    [InlineData("invalid-input-secret", ReCaptchaError.InvalidInputSecret)]
    [InlineData("missing-input-response", ReCaptchaError.MissingInputResponse)]
    [InlineData("invalid-input-response", ReCaptchaError.InvalidInputResponse)]
    [InlineData("bad-request", ReCaptchaError.BadRequest)]
    [InlineData("timeout-or-duplicate", ReCaptchaError.TimeOutOrDuplicate)]
    public void maps_each_known_code_to_its_enum_value(string code, ReCaptchaError expected)
    {
        new[] { code }.ToReCaptchaErrors().Should().ContainSingle().Which.Should().Be(expected);
    }

    [Theory]
    [InlineData("totally-unknown")]
    [InlineData("Bad-Request")] // case-sensitive: the upper-cased variant is not a known code
    [InlineData("")]
    public void maps_unrecognized_code_to_unknown(string code)
    {
        new[] { code }.ToReCaptchaErrors().Should().ContainSingle().Which.Should().Be(ReCaptchaError.Unknown);
    }

    [Fact]
    public void null_input_yields_empty_array()
    {
        ((string[]?)null).ToReCaptchaErrors().Should().BeEmpty();
    }

    [Fact]
    public void empty_input_yields_empty_array()
    {
        Array.Empty<string>().ToReCaptchaErrors().Should().BeEmpty();
    }

    [Fact]
    public void preserves_order_of_multiple_codes()
    {
        var result = new[] { "bad-request", "timeout-or-duplicate", "missing-input-secret" }.ToReCaptchaErrors();

        result
            .Should()
            .Equal(ReCaptchaError.BadRequest, ReCaptchaError.TimeOutOrDuplicate, ReCaptchaError.MissingInputSecret);
    }

    [Fact]
    public void result_extension_maps_error_codes_from_recaptcha_result()
    {
        var result = new ReCaptchaV2VerifyResult
        {
            Success = false,
            ErrorCodes = ["bad-request", "timeout-or-duplicate"],
        };

        result.ToReCaptchaErrors().Should().Equal(ReCaptchaError.BadRequest, ReCaptchaError.TimeOutOrDuplicate);
    }

    [Fact]
    public void result_extension_returns_empty_when_no_error_codes()
    {
        var result = new ReCaptchaV2VerifyResult { Success = true };

        result.ToReCaptchaErrors().Should().BeEmpty();
    }

    [Fact]
    public void result_extension_throws_for_marker_impl_not_deriving_from_captcha_result()
    {
        IReCaptchaVerifyResult stray = new StrayReCaptchaResult();

        var act = () => stray.ToReCaptchaErrors();

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class StrayReCaptchaResult : IReCaptchaVerifyResult;
}
