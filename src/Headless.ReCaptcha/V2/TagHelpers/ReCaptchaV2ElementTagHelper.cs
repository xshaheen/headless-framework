// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V2.TagHelpers;

/// <summary>
/// Razor tag helper that injects reCAPTCHA v2 widget attributes onto any existing HTML element. Apply any
/// of the <c>recaptcha-v2-*</c> attributes to an element to activate the helper.
/// </summary>
[PublicAPI]
[HtmlTargetElement("*", Attributes = _BadgeAttributeName)]
[HtmlTargetElement("*", Attributes = _ThemeAttributeName)]
[HtmlTargetElement("*", Attributes = _SizeAttributeName)]
[HtmlTargetElement("*", Attributes = _TabIndexAttributeName)]
[HtmlTargetElement("*", Attributes = _CallbackAttributeName)]
[HtmlTargetElement("*", Attributes = _ExpiredCallbackAttributeName)]
[HtmlTargetElement("*", Attributes = _ErrorCallbackAttributeName)]
public sealed class ReCaptchaV2ElementTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private const string _BadgeAttributeName = "recaptcha-v2-badge";
    private const string _ThemeAttributeName = "recaptcha-v2-theme";
    private const string _SizeAttributeName = "recaptcha-v2-size";
    private const string _TabIndexAttributeName = "recaptcha-v2-tab-index";
    private const string _CallbackAttributeName = "recaptcha-v2-callback";
    private const string _ExpiredCallbackAttributeName = "recaptcha-v2-expired-callback";
    private const string _ErrorCallbackAttributeName = "recaptcha-v2-error-callback";
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(SetupReCaptcha.V2Name);

    /// <summary>Maps to <c>recaptcha-v2-badge</c>. Controls badge position for invisible reCAPTCHA (<c>bottomright</c>, <c>bottomleft</c>, or <c>inline</c>).</summary>
    [HtmlAttributeName(_BadgeAttributeName)]
    public string? Badge { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-theme</c>. The color scheme of the widget (<c>light</c> or <c>dark</c>).</summary>
    [HtmlAttributeName(_ThemeAttributeName)]
    public string? Theme { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-size</c>. The size of the widget (<c>normal</c>, <c>compact</c>, or <c>invisible</c>).</summary>
    [HtmlAttributeName(_SizeAttributeName)]
    public string? Size { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-tab-index</c>. The tab index of the widget.</summary>
    [HtmlAttributeName(_TabIndexAttributeName)]
    public string? TabIndex { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-callback</c>. Name of the JavaScript callback invoked on successful response.</summary>
    [HtmlAttributeName(_CallbackAttributeName)]
    public string? Callback { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-expired-callback</c>. Name of the JavaScript callback invoked when the response expires.</summary>
    [HtmlAttributeName(_ExpiredCallbackAttributeName)]
    public string? ExpiredCallback { get; set; }

    /// <summary>Maps to <c>recaptcha-v2-error-callback</c>. Name of the JavaScript callback invoked on error.</summary>
    [HtmlAttributeName(_ErrorCallbackAttributeName)]
    public string? ErrorCallback { get; set; }

    /// <summary>Injects reCAPTCHA v2 widget data attributes onto the target element.</summary>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
        <div class="g-recaptcha"
           data-sitekey="_your_site_key_"
           data-callback="onSubmit"
           data-size="invisible">
           ....
        </div>
        */

        ReCaptchaV2Widget.ApplyAttributes(
            output,
            _options.SiteKey,
            Badge,
            Theme,
            Size,
            TabIndex,
            Callback,
            ExpiredCallback,
            ErrorCallback
        );
    }
}
