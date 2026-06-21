// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>reCAPTCHA tag-helper rendering for v2 (div + script) and v3 (script + programmatic script).</summary>
public sealed class ReCaptchaTagHelperTests
{
    [Fact]
    public void v2_div_helper_emits_g_recaptcha_div_with_sitekey()
    {
        var helper = new ReCaptchaV2DivTagHelper(_Options(CaptchaConstants.ReCaptchaV2Provider, "v2-site"))
        {
            Callback = "onSubmit",
        };
        var output = _NewOutput("recaptcha-div-v2");

        helper.Process(_NewContext(), output);

        output.TagName.Should().Be("div");
        output.Attributes["class"].Value.Should().Be("g-recaptcha");
        output.Attributes["data-sitekey"].Value.Should().Be("v2-site");
        output.Attributes["data-callback"].Value.Should().Be("onSubmit");
    }

    [Fact]
    public void v2_script_helper_emits_recaptcha_api_js()
    {
        var helper = new ReCaptchaV2ScriptTagHelper(
            _Options(CaptchaConstants.ReCaptchaV2Provider, "v2-site"),
            _Language("en-US")
        );
        var output = _NewOutput("recaptcha-script-v2");

        helper.Process(_NewContext(), output);

        output.Content.GetContent().Should().Contain("recaptcha/api.js");
    }

    [Fact]
    public void v3_script_helper_emits_render_with_sitekey()
    {
        var helper = new ReCaptchaV3ScriptTagHelper(
            _Options(CaptchaConstants.ReCaptchaV3Provider, "v3-site"),
            _Language("en-US")
        );
        var output = _NewOutput("recaptcha-script-v3");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().Contain("recaptcha/api.js");
        content.Should().Contain("render=v3-site");
    }

    [Fact]
    public void v3_script_js_helper_emits_execute_script_with_sitekey()
    {
        var helper = new ReCaptchaV3ScriptJsTagHelper(_Options(CaptchaConstants.ReCaptchaV3Provider, "v3-site"))
        {
            Action = "login",
            Callback = "onToken",
        };
        var output = _NewOutput("recaptcha-script-v3-js");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().Contain("grecaptcha.execute('v3-site'");
        content.Should().Contain("action:'login'");
        content.Should().Contain("onToken(token)");
    }

    private static IOptionsSnapshot<ReCaptchaOptions> _Options(string name, string siteKey)
    {
        var snapshot = Substitute.For<IOptionsSnapshot<ReCaptchaOptions>>();
        snapshot.Get(name).Returns(new ReCaptchaOptions { SiteKey = siteKey, SiteSecret = "secret" });

        return snapshot;
    }

    private static IReCaptchaLanguageCodeProvider _Language(string code)
    {
        var provider = Substitute.For<IReCaptchaLanguageCodeProvider>();
        provider.GetLanguageCode().Returns(code);

        return provider;
    }

    private static TagHelperContext _NewContext() =>
        new(new TagHelperAttributeList(), new Dictionary<object, object>(), "test-id");

    private static TagHelperOutput _NewOutput(string tagName) =>
        new(
            tagName,
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );
}
