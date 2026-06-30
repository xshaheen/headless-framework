// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
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

        // The src is emitted as an encoded output attribute (not raw SetHtmlContent), so the <script> tag renders.
        output.TagName.Should().Be("script");
        _Src(output).Should().Contain("recaptcha/api.js");
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

        output.TagName.Should().Be("script");
        var src = _Src(output);
        src.Should().Contain("recaptcha/api.js");
        src.Should().Contain("render=v3-site");
    }

    [Fact]
    public void v3_script_js_helper_throws_when_callback_is_not_js_identifier()
    {
        var helper = new ReCaptchaV3ScriptJsTagHelper(_Options(CaptchaConstants.ReCaptchaV3Provider, "v3-site"))
        {
            Callback = "alert(1)",
        };
        var output = _NewOutput("recaptcha-script-v3-js");

        var act = () => helper.Process(_NewContext(), output);

        act.Should().Throw<InvalidOperationException>().WithMessage("*alert(1)*");
    }

    [Fact]
    public void v3_script_js_helper_encodes_action_to_prevent_js_breakout()
    {
        var helper = new ReCaptchaV3ScriptJsTagHelper(_Options(CaptchaConstants.ReCaptchaV3Provider, "v3-site"))
        {
            Action = "lo'gin",
            Callback = "onToken",
        };
        var output = _NewOutput("recaptcha-script-v3-js");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().NotContain("lo'gin");
        content.Should().Contain(JavaScriptEncoder.Default.Encode("lo'gin"));
    }

    [Fact]
    public void v3_script_js_helper_encodes_site_key_to_prevent_js_breakout()
    {
        var helper = new ReCaptchaV3ScriptJsTagHelper(_Options(CaptchaConstants.ReCaptchaV3Provider, "ab'cd"))
        {
            Callback = "onToken",
        };
        var output = _NewOutput("recaptcha-script-v3-js");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().NotContain("ab'cd");
        content.Should().Contain(JavaScriptEncoder.Default.Encode("ab'cd"));
    }

    [Fact]
    public void v3_script_js_helper_execute_false_emits_callback_parameter_form()
    {
        var helper = new ReCaptchaV3ScriptJsTagHelper(_Options(CaptchaConstants.ReCaptchaV3Provider, "v3-site"))
        {
            Execute = false,
            Callback = "onToken",
        };
        var output = _NewOutput("recaptcha-script-v3-js");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().Contain("function(callback)");
        content.Should().Contain(".then(callback)");
    }

    [Fact]
    public void v2_element_helper_injects_recaptcha_attributes_onto_target()
    {
        var helper = new ReCaptchaV2ElementTagHelper(_Options(CaptchaConstants.ReCaptchaV2Provider, "v2-site"))
        {
            Theme = "dark",
            Size = "invisible",
            Callback = "onSubmit",
        };
        var output = _NewOutput("button");

        helper.Process(_NewContext(), output);

        output.Attributes["class"].Value.Should().Be("g-recaptcha");
        output.Attributes["data-sitekey"].Value.Should().Be("v2-site");
        output.Attributes["data-theme"].Value.Should().Be("dark");
        output.Attributes["data-size"].Value.Should().Be("invisible");
        output.Attributes["data-callback"].Value.Should().Be("onSubmit");
    }

    [Fact]
    public void v2_element_helper_throws_when_default_provider_not_registered()
    {
        var helper = new ReCaptchaV2ElementTagHelper(_Options(CaptchaConstants.ReCaptchaV2Provider, ""));
        var output = _NewOutput("button");

        var act = () => helper.Process(_NewContext(), output);

        act.Should().Throw<InvalidOperationException>();
    }

    private static string _Src(TagHelperOutput output) => output.Attributes["src"].Value?.ToString() ?? "";

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

    private static TagHelperContext _NewContext()
    {
        return new([], new Dictionary<object, object>(), "test-id");
    }

    private static TagHelperOutput _NewOutput(string tagName)
    {
        return new(tagName, [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    }
}
