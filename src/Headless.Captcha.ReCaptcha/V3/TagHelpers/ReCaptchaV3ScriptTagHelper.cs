// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Razor tag helper that renders the reCAPTCHA v3 API script tag with the site key pre-embedded. Use as
/// <c>&lt;recaptcha-script-v3 /&gt;</c> in Razor views, typically in the page head.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV3ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV3Provider);

    /// <summary>
    /// When <see langword="true"/>, injects an inline <c>&lt;style&gt;</c> that hides the reCAPTCHA badge
    /// (<c>.grecaptcha-badge { visibility: hidden; }</c>). Per Google policy, hiding the badge requires
    /// displaying the reCAPTCHA branding in the page text.
    /// </summary>
    public bool HideBadge { get; set; }

    /// <summary>Renders the reCAPTCHA v3 script tag with the site key and language query parameters.</summary>
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
            .SetQueryParam("hl", reCaptchaLanguageCodeProvider.GetLanguageCode())
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
