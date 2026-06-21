// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V2.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-div-v2", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV2DivTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(SetupReCaptcha.V2Name);

    public string? Badge { get; set; }

    public string? Theme { get; set; }

    public string? Size { get; set; }

    public string? TabIndex { get; set; }

    public string? Callback { get; set; }

    public string? ExpiredCallback { get; set; }

    public string? ErrorCallback { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
        <div class="g-recaptcha"
           data-sitekey="_your_site_key_"
           data-callback="onSubmit"
           data-size="invisible">
           ....
        </div>
        */

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        ReCaptchaV2Widget.ApplyAttributes(
            output,
            _options.SiteKey,
            Badge,
            Theme,
            Size,
            TabIndex,
            Callback,
            ExpiredCallback,
            ErrorCallback
        );
    }
}
