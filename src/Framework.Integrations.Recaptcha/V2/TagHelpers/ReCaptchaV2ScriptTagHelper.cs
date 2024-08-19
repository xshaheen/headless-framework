using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.Internals;
using Framework.Integrations.Recaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Integrations.Recaptcha.V2.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaSettings> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    public bool ScriptAsync { get; set; } = true;

    public bool ScriptDefer { get; set; } = true;

    public string? Onload { get; set; }

    public string? Render { get; set; }

    public bool HideBadge { get; set; }

    private readonly ReCaptchaSettings _settings = optionsAccessor.Get(ReCaptchaConstants.V2);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://www.google.com/recaptcha/api.js" async defer></script>
        */

        output.TagName = "";

        var src =
            $"{_settings.VerifyBaseUrl.RemovePostFix(StringComparison.OrdinalIgnoreCase, "/")}/recaptcha/api.js?"
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
