// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Renders the reCAPTCHA v2 API <c>&lt;script&gt;</c> tag.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    ICaptchaLanguageCodeProvider languageCodeProvider
) : TagHelper
{
    /// <summary>Gets or sets a value indicating whether to add the <c>async</c> attribute to the script tag.</summary>
    public bool ScriptAsync { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to add the <c>defer</c> attribute to the script tag.</summary>
    public bool ScriptDefer { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the JavaScript function called when the reCAPTCHA API loads, mapped to the
    /// <c>onload</c> query parameter.
    /// </summary>
    public string? Onload { get; set; }

    /// <summary>
    /// Gets or sets the rendering mode or site key, mapped to the <c>render</c> query parameter.
    /// </summary>
    public string? Render { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to inject inline CSS to hide the reCAPTCHA badge.
    /// </summary>
    public bool HideBadge { get; set; }

    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV2Provider);

    /// <summary>Renders the reCAPTCHA v2 script tag with language and optional query parameters.</summary>
    /// <param name="context">Contains information associated with the current HTML tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    /// <exception cref="InvalidOperationException">The reCAPTCHA v2 site key is not configured.</exception>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://www.google.com/recaptcha/api.js" async defer></script>
        */

        // The script helpers render the default provider, like the div/element helpers. A missing/empty SiteKey means
        // no default was registered (named-only), so fail consistently instead of silently emitting the API script.
        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "reCAPTCHA v2 tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseReCaptchaV2(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        var src = Url.Parse(_options.VerifyBaseUrl.TrimEnd('/') + "/recaptcha/api.js")
            .SetQueryParam("hl", languageCodeProvider.GetLanguageCode())
            .SetQueryParam("onload", string.IsNullOrWhiteSpace(Onload) ? null : Onload)
            .SetQueryParam("render", string.IsNullOrWhiteSpace(Render) ? null : Render)
            .ToString();

        // Emit through the tag-helper output API so the framework HTML-encodes the attribute value (no raw
        // SetHtmlContent string-concatenation of the URL into the <script> markup).
        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        output.Attributes.Add("src", src);

        if (ScriptAsync)
        {
            output.Attributes.Add(new TagHelperAttribute("async"));
        }

        if (ScriptDefer)
        {
            output.Attributes.Add(new TagHelperAttribute("defer"));
        }

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
