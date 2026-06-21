// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.ReCaptcha;
using Headless.ReCaptcha.V2.TagHelpers;
using Headless.ReCaptcha.V3.TagHelpers;

namespace Tests;

public sealed class ReCaptchaV3ScriptJsTagHelperTests
{
    private static ReCaptchaV3ScriptJsTagHelper Create(string siteKey = "site-key")
    {
        var options = TestHelpers.Options(siteKey: siteKey);
        return new ReCaptchaV3ScriptJsTagHelper(TestHelpers.Snapshot(SetupReCaptcha.V3Name, options));
    }

    [Fact]
    public void should_emit_execute_call_with_action_and_callback_when_execute_true()
    {
        var helper = Create();
        helper.Action = "login";
        helper.Callback = "onVerified";
        helper.Execute = true;

        var output = TestHelpers.TagOutput("recaptcha-script-v3-js");
        helper.Process(TestHelpers.TagContext(), output);

        var script = output.Content.GetContent();
        script.Should().Contain("grecaptcha.execute('site-key'");
        script.Should().Contain("action:'login'");
        script.Should().Contain("onVerified(token)");
        script.Should().Contain("grecaptcha.reExecute()");
    }

    [Fact]
    public void should_use_callback_parameter_when_execute_false()
    {
        var helper = Create();
        helper.Action = "login";
        helper.Execute = false;

        var output = TestHelpers.TagOutput("recaptcha-script-v3-js");
        helper.Process(TestHelpers.TagContext(), output);

        var script = output.Content.GetContent();
        script.Should().Contain("function(callback)");
        script.Should().Contain(".then(callback)");
    }

    [Fact]
    public void should_throw_for_callback_that_is_not_a_js_identifier()
    {
        var helper = Create();
        helper.Callback = "alert(1)";

        var output = TestHelpers.TagOutput("recaptcha-script-v3-js");
        var act = () => helper.Process(TestHelpers.TagContext(), output);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_js_encode_action_to_prevent_breakout()
    {
        var helper = Create();
        helper.Action = "lo'gin"; // a quote would break out of the 'action' literal if unencoded
        helper.Callback = "onVerified";

        var output = TestHelpers.TagOutput("recaptcha-script-v3-js");
        helper.Process(TestHelpers.TagContext(), output);

        var script = output.Content.GetContent();
        script.Should().NotContain("lo'gin"); // the raw, unencoded quote must not appear in the JS literal
        script.Should().Contain(JavaScriptEncoder.Default.Encode("lo'gin"));
    }

    [Fact]
    public void should_js_encode_site_key_to_prevent_breakout()
    {
        var helper = Create(siteKey: "ab'cd");
        helper.Action = "login";
        helper.Callback = "onVerified";

        var output = TestHelpers.TagOutput("recaptcha-script-v3-js");
        helper.Process(TestHelpers.TagContext(), output);

        var script = output.Content.GetContent();
        script.Should().NotContain("ab'cd"); // the raw, unencoded quote must not appear in the JS literal
        script.Should().Contain(JavaScriptEncoder.Default.Encode("ab'cd"));
    }
}

public sealed class ReCaptchaV3ScriptTagHelperTests
{
    [Fact]
    public void should_build_api_script_without_double_slash_and_with_encoded_params()
    {
        var helper = new ReCaptchaV3ScriptTagHelper(
            TestHelpers.Snapshot(SetupReCaptcha.V3Name, TestHelpers.Options()),
            TestHelpers.LanguageProvider("en-US")
        );

        var output = TestHelpers.TagOutput("recaptcha-script-v3");
        helper.Process(TestHelpers.TagContext(), output);

        var html = output.Render();
        html.Should().Contain("https://www.google.com/recaptcha/api.js?hl=en-US&render=site-key");
        html.Should().NotContain(".com//recaptcha");
    }

    [Fact]
    public void should_emit_hide_badge_style_when_requested()
    {
        var helper = new ReCaptchaV3ScriptTagHelper(
            TestHelpers.Snapshot(SetupReCaptcha.V3Name, TestHelpers.Options()),
            TestHelpers.LanguageProvider("en")
        )
        {
            HideBadge = true,
        };

        var output = TestHelpers.TagOutput("recaptcha-script-v3");
        helper.Process(TestHelpers.TagContext(), output);

        output.Render().Should().Contain(".grecaptcha-badge{visibility:hidden;}");
    }
}

public sealed class ReCaptchaV2ScriptTagHelperTests
{
    [Fact]
    public void should_emit_async_and_defer_by_default()
    {
        var helper = new ReCaptchaV2ScriptTagHelper(
            TestHelpers.Snapshot(SetupReCaptcha.V2Name, TestHelpers.Options()),
            TestHelpers.LanguageProvider("en")
        );

        var output = TestHelpers.TagOutput("recaptcha-script-v2");
        helper.Process(TestHelpers.TagContext(), output);

        var html = output.Content.GetContent();
        html.Should().Contain("async");
        html.Should().Contain("defer");
        html.Should().Contain("recaptcha/api.js?hl=en");
    }

    [Fact]
    public void should_append_onload_and_render_params_when_set()
    {
        var helper = new ReCaptchaV2ScriptTagHelper(
            TestHelpers.Snapshot(SetupReCaptcha.V2Name, TestHelpers.Options()),
            TestHelpers.LanguageProvider("en")
        )
        {
            Onload = "onLoadCb",
            Render = "explicit",
        };

        var output = TestHelpers.TagOutput("recaptcha-script-v2");
        helper.Process(TestHelpers.TagContext(), output);

        var html = output.Content.GetContent();
        html.Should().Contain("onload=onLoadCb");
        html.Should().Contain("render=explicit");
    }
}

public sealed class ReCaptchaV2WidgetTagHelperTests
{
    [Fact]
    public void div_helper_should_emit_div_with_sitekey_and_set_optional_attributes()
    {
        var helper = new ReCaptchaV2DivTagHelper(TestHelpers.Snapshot(SetupReCaptcha.V2Name, TestHelpers.Options()))
        {
            Theme = "dark",
            Callback = "onSubmit",
        };

        var output = TestHelpers.TagOutput("recaptcha-div-v2");
        helper.Process(TestHelpers.TagContext(), output);

        output.TagName.Should().Be("div");
        output.Attributes.ContainsName("class").Should().BeTrue();
        output.Attributes["data-sitekey"].Value.Should().Be("site-key");
        output.Attributes["data-theme"].Value.Should().Be("dark");
        output.Attributes["data-callback"].Value.Should().Be("onSubmit");
        output.Attributes.ContainsName("data-size").Should().BeFalse();
    }

    [Fact]
    public void element_helper_should_add_recaptcha_class_and_sitekey()
    {
        var helper = new ReCaptchaV2ElementTagHelper(
            TestHelpers.Snapshot(SetupReCaptcha.V2Name, TestHelpers.Options())
        );

        var output = TestHelpers.TagOutput("div");
        helper.Process(TestHelpers.TagContext(), output);

        output.Attributes["class"].Value.Should().Be("g-recaptcha");
        output.Attributes["data-sitekey"].Value.Should().Be("site-key");
    }
}
