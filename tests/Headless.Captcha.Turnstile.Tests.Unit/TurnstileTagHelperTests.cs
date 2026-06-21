// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.Captcha;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Turnstile tag-helper rendering: script URL and the cf-turnstile widget attributes.</summary>
public sealed class TurnstileTagHelperTests
{
    [Fact]
    public void script_helper_emits_api_js_url_without_explicit_render_by_default()
    {
        var helper = new TurnstileScriptTagHelper(_Options(new TurnstileOptions { SiteKey = "k", SiteSecret = "s" }));
        var output = _NewOutput("turnstile-script");

        helper.Process(_NewContext(), output);

        var content = output.Content.GetContent();
        content.Should().Contain("turnstile/v0/api.js");
        content.Should().NotContain("render=explicit");
    }

    [Fact]
    public void script_helper_appends_render_explicit_when_set()
    {
        var helper = new TurnstileScriptTagHelper(_Options(new TurnstileOptions { SiteKey = "k", SiteSecret = "s" }))
        {
            ExplicitRender = true,
        };
        var output = _NewOutput("turnstile-script");

        helper.Process(_NewContext(), output);

        output.Content.GetContent().Should().Contain("render=explicit");
    }

    [Fact]
    public void widget_helper_emits_cf_turnstile_div_with_attributes()
    {
        var languageProvider = Substitute.For<ITurnstileLanguageCodeProvider>();
        languageProvider.GetLanguageCode().Returns("en-US");

        var helper = new TurnstileWidgetTagHelper(
            _Options(new TurnstileOptions { SiteKey = "site-123", SiteSecret = "s" }),
            languageProvider
        )
        {
            Theme = "dark",
            Size = "compact",
            Action = "login",
            Callback = "onOk",
            CData = "cd-1",
        };
        var output = _NewOutput("turnstile-widget");

        helper.Process(_NewContext(), output);

        output.TagName.Should().Be("div");
        output.Attributes["class"].Value.Should().Be("cf-turnstile");
        output.Attributes["data-sitekey"].Value.Should().Be("site-123");
        output.Attributes["data-theme"].Value.Should().Be("dark");
        output.Attributes["data-size"].Value.Should().Be("compact");
        output.Attributes["data-action"].Value.Should().Be("login");
        output.Attributes["data-callback"].Value.Should().Be("onOk");
        output.Attributes["data-cdata"].Value.Should().Be("cd-1");
        output.Attributes["data-language"].Value.Should().Be("en-US");
    }

    private static IOptionsSnapshot<TurnstileOptions> _Options(TurnstileOptions options)
    {
        var snapshot = Substitute.For<IOptionsSnapshot<TurnstileOptions>>();
        snapshot.Get(CaptchaConstants.TurnstileProvider).Returns(options);

        return snapshot;
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
