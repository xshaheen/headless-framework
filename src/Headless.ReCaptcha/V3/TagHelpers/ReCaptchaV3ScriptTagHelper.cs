// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Recaptcha.Contracts;
using Headless.Recaptcha.Internals;
using Headless.Recaptcha.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Recaptcha.V3.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV3ScriptTagHelper(
    IOptionsSnapshot<ReCaptchaOptions> optionsAccessor,
    IReCaptchaLanguageCodeProvider reCaptchaLanguageCodeProvider
) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaSetup.V3Name);

    public bool HideBadge { get; set; }

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
