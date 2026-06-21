// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Internals;
using Headless.ReCaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V3.TagHelpers;

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
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(SetupReCaptcha.V3Name);

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
        output.TagName = "";

        var src =
            $"{_options.VerifyBaseUrl.RemovePostFix(StringComparison.OrdinalIgnoreCase, "/")}/recaptcha/api.js?hl={Uri.EscapeDataString(reCaptchaLanguageCodeProvider.GetLanguageCode())}&render={Uri.EscapeDataString(_options.SiteKey)}";

        output.Content.SetHtmlContent($"<script src=\"{src}\"></script>");

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
