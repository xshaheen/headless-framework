// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Razor tag helper that renders a <c>&lt;div&gt;</c> element pre-configured with all reCAPTCHA v2 widget
/// attributes. Use as <c>&lt;recaptcha-div-v2 /&gt;</c> in Razor views.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-div-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2DivTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV2Provider);

    /// <summary>Maps to <c>data-badge</c>. Controls the badge position for invisible reCAPTCHA (<c>bottomright</c>, <c>bottomleft</c>, or <c>inline</c>).</summary>
    public string? Badge { get; set; }

    /// <summary>Maps to <c>data-theme</c>. The color scheme of the widget (<c>light</c> or <c>dark</c>).</summary>
    public string? Theme { get; set; }

    /// <summary>Maps to <c>data-size</c>. The size of the widget (<c>normal</c>, <c>compact</c>, or <c>invisible</c>).</summary>
    public string? Size { get; set; }

    /// <summary>Maps to <c>data-tabindex</c>. The tab index of the widget.</summary>
    public string? TabIndex { get; set; }

    /// <summary>Maps to <c>data-callback</c>. Name of the JavaScript callback invoked when the user submits a successful response.</summary>
    public string? Callback { get; set; }

    /// <summary>Maps to <c>data-expired-callback</c>. Name of the JavaScript callback invoked when the reCAPTCHA response expires.</summary>
    public string? ExpiredCallback { get; set; }

    /// <summary>Maps to <c>data-error-callback</c>. Name of the JavaScript callback invoked when reCAPTCHA encounters an error.</summary>
    public string? ErrorCallback { get; set; }

    /// <summary>Renders the reCAPTCHA v2 widget container element with the configured data attributes.</summary>
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

        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "reCAPTCHA v2 tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseReCaptchaV2(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

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
