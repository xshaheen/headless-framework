// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha.V2.TagHelpers;

[PublicAPI]
[HtmlTargetElement("*", Attributes = _BadgeAttributeName)]
[HtmlTargetElement("*", Attributes = _ThemeAttributeName)]
[HtmlTargetElement("*", Attributes = _SizeAttributeName)]
[HtmlTargetElement("*", Attributes = _TabIndexAttributeName)]
[HtmlTargetElement("*", Attributes = _CallbackAttributeName)]
[HtmlTargetElement("*", Attributes = _ExpiredCallbackAttributeName)]
[HtmlTargetElement("*", Attributes = _ErrorCallbackAttributeName)]
public sealed class ReCaptchaV2ElementTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private const string _BadgeAttributeName = "recaptcha-v2-badge";
    private const string _ThemeAttributeName = "recaptcha-v2-theme";
    private const string _SizeAttributeName = "recaptcha-v2-size";
    private const string _TabIndexAttributeName = "recaptcha-v2-tab-index";
    private const string _CallbackAttributeName = "recaptcha-v2-callback";
    private const string _ExpiredCallbackAttributeName = "recaptcha-v2-expired-callback";
    private const string _ErrorCallbackAttributeName = "recaptcha-v2-error-callback";
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaConstants.V2);

    [HtmlAttributeName(_BadgeAttributeName)]
    public string? Badge { get; set; }

    [HtmlAttributeName(_ThemeAttributeName)]
    public string? Theme { get; set; }

    [HtmlAttributeName(_SizeAttributeName)]
    public string? Size { get; set; }

    [HtmlAttributeName(_TabIndexAttributeName)]
    public string? TabIndex { get; set; }

    [HtmlAttributeName(_CallbackAttributeName)]
    public string? Callback { get; set; }

    [HtmlAttributeName(_ExpiredCallbackAttributeName)]
    public string? ExpiredCallback { get; set; }

    [HtmlAttributeName(_ErrorCallbackAttributeName)]
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

        output.Attributes.Add("class", "g-recaptcha");
        output.Attributes.Add("data-sitekey", _options.SiteKey);

        if (!string.IsNullOrWhiteSpace(Badge))
        {
            output.Attributes.Add("data-badge", Badge);
        }

        if (!string.IsNullOrWhiteSpace(Theme))
        {
            output.Attributes.Add("data-theme", Theme);
        }

        if (!string.IsNullOrWhiteSpace(Size))
        {
            output.Attributes.Add("data-size", Size);
        }

        if (!string.IsNullOrWhiteSpace(TabIndex))
        {
            output.Attributes.Add("data-tabindex", TabIndex);
        }

        if (!string.IsNullOrWhiteSpace(Callback))
        {
            output.Attributes.Add("data-callback", Callback);
        }

        if (!string.IsNullOrWhiteSpace(ExpiredCallback))
        {
            output.Attributes.Add("data-expired-callback", ExpiredCallback);
        }

        if (!string.IsNullOrWhiteSpace(ErrorCallback))
        {
            output.Attributes.Add("data-error-callback", ErrorCallback);
        }
    }
}
