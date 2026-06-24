// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Razor tag helper that renders the reCAPTCHA v2 API script tag. Use as
/// <c>&lt;recaptcha-script-v2 /&gt;</c> in Razor views, typically in the page head or just before the
/// closing <c>&lt;/body&gt;</c>.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    /// <summary>When <see langword="true"/> (the default), adds the <c>async</c> attribute to the rendered script tag.</summary>
    public bool ScriptAsync { get; set; } = true;

    /// <summary>When <see langword="true"/> (the default), adds the <c>defer</c> attribute to the rendered script tag.</summary>
    public bool ScriptDefer { get; set; } = true;

    /// <summary>
    /// Maps to the <c>onload</c> query parameter. The name of a JavaScript function called once the
    /// reCAPTCHA API is ready.
    /// </summary>
    public string? Onload { get; set; }

    /// <summary>
    /// Maps to the <c>render</c> query parameter. Set to <c>explicit</c> to disable automatic widget
    /// rendering, or to a site key to render a specific widget on load.
    /// </summary>
    public string? Render { get; set; }

    /// <summary>
    /// When <see langword="true"/>, injects an inline <c>&lt;style&gt;</c> that hides the reCAPTCHA badge
    /// (<c>.grecaptcha-badge { visibility: hidden; }</c>). Per Google policy, hiding the badge requires
    /// displaying the reCAPTCHA branding in the page text.
    /// </summary>
    public bool HideBadge { get; set; }

    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV2Provider);

    /// <summary>Renders the reCAPTCHA v2 script tag with language and optional query parameters.</summary>
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
            .SetQueryParam("hl", reCaptchaLanguageCodeProvider.GetLanguageCode())
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
