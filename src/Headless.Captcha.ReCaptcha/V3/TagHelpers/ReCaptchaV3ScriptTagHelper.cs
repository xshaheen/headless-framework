// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Renders the reCAPTCHA v3 API <c>&lt;script&gt;</c> tag with the configured site key.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV3ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    ICaptchaLanguageCodeProvider languageCodeProvider
) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV3Provider);

    /// <summary>
    /// Gets or sets a value indicating whether to inject inline CSS to hide the reCAPTCHA badge.
    /// </summary>
    public bool HideBadge { get; set; }

    /// <summary>Renders the reCAPTCHA v3 script tag with site key and language query parameters.</summary>
    /// <param name="context">Contains information associated with the current HTML tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    /// <exception cref="InvalidOperationException">The reCAPTCHA v3 site key is not configured.</exception>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://www.google.com/recaptcha/api.js?render=_reCAPTCHA_site_key"></script>
        */

        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "reCAPTCHA v3 tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseReCaptchaV3(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        var src = Url.Parse(_options.VerifyBaseUrl.TrimEnd('/') + "/recaptcha/api.js")
            .SetQueryParam("hl", languageCodeProvider.GetLanguageCode())
            .SetQueryParam("render", _options.SiteKey)
            .ToString();

        // Emit through the tag-helper output API so the framework HTML-encodes the attribute value (no raw
        // SetHtmlContent string-concatenation of the URL into the <script> markup).
        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        output.Attributes.Add("src", src);

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
