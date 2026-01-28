// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Recaptcha.Contracts;
using Headless.Recaptcha.Internals;
using Headless.Recaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Recaptcha.V2.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    public bool ScriptAsync { get; set; } = true;

    public bool ScriptDefer { get; set; } = true;

    public string? Onload { get; set; }

    public string? Render { get; set; }

    public bool HideBadge { get; set; }

    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaSetup.V2Name);

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

        var scriptAsync = ScriptAsync ? "async" : "";
        var scriptDefer = ScriptDefer ? "defer" : "";

        output.Content.SetHtmlContent($"<script {scriptAsync} {scriptDefer} src=\"{src}\"></script>");

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
