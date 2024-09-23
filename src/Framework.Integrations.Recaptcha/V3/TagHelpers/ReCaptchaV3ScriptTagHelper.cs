// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.Internals;
using Framework.Integrations.Recaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Integrations.Recaptcha.V3.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV3ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaSettings> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    private readonly ReCaptchaSettings _settings = optionsAccessor.Get(ReCaptchaConstants.V3);

    public bool HideBadge { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://www.google.com/recaptcha/api.js?render=_reCAPTCHA_site_key"></script>
        */
        output.TagName = "";

        var src =
            $"{_settings.VerifyBaseUrl.RemovePostFix(StringComparison.OrdinalIgnoreCase, "/")}/recaptcha/api.js?hl={reCaptchaLanguageCodeProvider.GetLanguageCode()}&render={_settings.SiteKey}";

        output.Content.SetHtmlContent($"<script src=\"{src}\"></script>");

        if (HideBadge)
        {
            output.PostElement.SetHtmlContent("<style>.grecaptcha-badge{visibility:hidden;}</style>");
        }
    }
}
