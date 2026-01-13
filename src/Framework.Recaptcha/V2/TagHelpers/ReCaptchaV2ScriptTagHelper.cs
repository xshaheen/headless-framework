// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.Internals;
using Framework.Recaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha.V2.TagHelpers;

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

    private readonly ReCaptchaOptions _options = optionsAccessor.Get(RecaptchaSetup.V2Name);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://www.google.com/recaptcha/api.js" async defer></script>
        */

        output.TagName = "";

        var src =
            $"{_options.VerifyBaseUrl.RemovePostFix(StringComparison.OrdinalIgnoreCase, "/")}/recaptcha/api.js?"
            + $"hl={reCaptchaLanguageCodeProvider.GetLanguageCode()}";

        if (!string.IsNullOrWhiteSpace(Onload))
        {
            src += $"&onload={Onload}";
        }

        if (!string.IsNullOrWhiteSpace(Render))
        {
            src += $"&render={Render}";
        }

        var scriptAsync = ScriptAsync ? "async" : string.Empty;
        var scriptDefer = ScriptDefer ? "defer" : string.Empty;

        output.Content.SetHtmlContent($"<script {scriptAsync} {scriptDefer} src=\"{src}\"></script>");

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
