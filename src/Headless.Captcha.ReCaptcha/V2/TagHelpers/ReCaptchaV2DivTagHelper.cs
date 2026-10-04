// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Renders a <c>&lt;div&gt;</c> element configured with reCAPTCHA v2 widget attributes.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-div-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2DivTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV2Provider);

    /// <summary>Gets or sets the badge position for invisible reCAPTCHA, mapped to <c>data-badge</c>.</summary>
    public string? Badge { get; set; }

    /// <summary>Gets or sets the color scheme of the widget, mapped to <c>data-theme</c>.</summary>
    public string? Theme { get; set; }

    /// <summary>Gets or sets the size of the widget, mapped to <c>data-size</c>.</summary>
    public string? Size { get; set; }

    /// <summary>Gets or sets the tab index of the widget, mapped to <c>data-tabindex</c>.</summary>
    public string? TabIndex { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked when a user submits a successful response, mapped to <c>data-callback</c>.</summary>
    public string? Callback { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked when the response expires, mapped to <c>data-expired-callback</c>.</summary>
    public string? ExpiredCallback { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked when reCAPTCHA encounters an error, mapped to <c>data-error-callback</c>.</summary>
    public string? ErrorCallback { get; set; }

    /// <summary>Renders the reCAPTCHA v2 widget container element with the configured data attributes.</summary>
    /// <param name="context">Contains information associated with the current HTML tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    /// <exception cref="InvalidOperationException">The reCAPTCHA v2 site key is not configured.</exception>
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
