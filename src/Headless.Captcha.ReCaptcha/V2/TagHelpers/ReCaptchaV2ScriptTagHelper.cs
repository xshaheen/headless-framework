// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        output.TagName = "";

        var baseUrl = _options.VerifyBaseUrl.RemovePostFix(StringComparison.OrdinalIgnoreCase, "/");
        var langCode = Uri.EscapeDataString(reCaptchaLanguageCodeProvider.GetLanguageCode());
        var onloadParam = string.IsNullOrWhiteSpace(Onload) ? "" : $"&onload={Uri.EscapeDataString(Onload)}";
        var renderParam = string.IsNullOrWhiteSpace(Render) ? "" : $"&render={Uri.EscapeDataString(Render)}";

        var src = $"{baseUrl}/recaptcha/api.js?hl={langCode}{onloadParam}{renderParam}";

        var attrs = string.Join(
            " ",
            new[] { ScriptAsync ? "async" : "", ScriptDefer ? "defer" : "", $"src=\"{src}\"" }.Where(a => a.Length > 0)
        );

        output.Content.SetHtmlContent($"<script {attrs}></script>");

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
