// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Injects reCAPTCHA v2 widget attributes onto an HTML element.
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
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV2Provider);

    /// <summary>Gets or sets the badge position for invisible reCAPTCHA, mapped to <c>recaptcha-v2-badge</c>.</summary>
    [HtmlAttributeName(_BadgeAttributeName)]
    public string? Badge { get; set; }

    /// <summary>Gets or sets the color scheme of the widget, mapped to <c>recaptcha-v2-theme</c>.</summary>
    [HtmlAttributeName(_ThemeAttributeName)]
    public string? Theme { get; set; }

    /// <summary>Gets or sets the size of the widget, mapped to <c>recaptcha-v2-size</c>.</summary>
    [HtmlAttributeName(_SizeAttributeName)]
    public string? Size { get; set; }

    /// <summary>Gets or sets the tab index of the widget, mapped to <c>recaptcha-v2-tab-index</c>.</summary>
    [HtmlAttributeName(_TabIndexAttributeName)]
    public string? TabIndex { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked on a successful response, mapped to <c>recaptcha-v2-callback</c>.</summary>
    [HtmlAttributeName(_CallbackAttributeName)]
    public string? Callback { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked when the response expires, mapped to <c>recaptcha-v2-expired-callback</c>.</summary>
    [HtmlAttributeName(_ExpiredCallbackAttributeName)]
    public string? ExpiredCallback { get; set; }

    /// <summary>Gets or sets the JavaScript callback function invoked on error, mapped to <c>recaptcha-v2-error-callback</c>.</summary>
    [HtmlAttributeName(_ErrorCallbackAttributeName)]
    public string? ErrorCallback { get; set; }

    /// <summary>Injects reCAPTCHA v2 widget data attributes onto the target element.</summary>
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
