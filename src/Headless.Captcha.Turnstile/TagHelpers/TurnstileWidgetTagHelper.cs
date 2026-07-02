// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Emits the Cloudflare Turnstile widget element — a <c>div.cf-turnstile</c> carrying <c>data-sitekey</c> (from the
/// default Turnstile options) and the optional theme/size/callback/action/cdata/language attributes. The language
/// defaults to <see cref="ITurnstileLanguageCodeProvider"/> and is rendered as <c>data-language</c> (Cloudflare's
/// widget language attribute).
/// </summary>
[PublicAPI]
[HtmlTargetElement("turnstile-widget", TagStructure = TagStructure.WithoutEndTag)]
public sealed class TurnstileWidgetTagHelper(
    IOptionsSnapshot<TurnstileOptions> optionsAccessor,
    ITurnstileLanguageCodeProvider languageCodeProvider
) : TagHelper
{
    private readonly TurnstileOptions _options = optionsAccessor.Get(CaptchaConstants.TurnstileProvider);

    public string? Theme { get; set; }

    public string? Size { get; set; }

    public string? Callback { get; set; }

    public string? ErrorCallback { get; set; }

    public string? ExpiredCallback { get; set; }

    public string? Action { get; set; }

    public string? CData { get; set; }

    /// <summary>Optional. Overrides the language code; defaults to <see cref="ITurnstileLanguageCodeProvider"/>.</summary>
    public string? Language { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
        <div class="cf-turnstile"
             data-sitekey="_your_site_key_"
             data-callback="onSubmit"
             data-theme="auto">
        </div>
        */

        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "Turnstile tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseTurnstile(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        output.Attributes.Add("class", "cf-turnstile");
        output.Attributes.Add("data-sitekey", _options.SiteKey);

        if (!string.IsNullOrWhiteSpace(Theme))
        {
            output.Attributes.Add("data-theme", Theme);
        }

        if (!string.IsNullOrWhiteSpace(Size))
        {
            output.Attributes.Add("data-size", Size);
        }

        if (!string.IsNullOrWhiteSpace(Callback))
        {
            output.Attributes.Add("data-callback", Callback);
        }

        if (!string.IsNullOrWhiteSpace(ErrorCallback))
        {
            output.Attributes.Add("data-error-callback", ErrorCallback);
        }

        if (!string.IsNullOrWhiteSpace(ExpiredCallback))
        {
            output.Attributes.Add("data-expired-callback", ExpiredCallback);
        }

        if (!string.IsNullOrWhiteSpace(Action))
        {
            output.Attributes.Add("data-action", Action);
        }

        if (!string.IsNullOrWhiteSpace(CData))
        {
            output.Attributes.Add("data-cdata", CData);
        }

        var language = string.IsNullOrWhiteSpace(Language) ? languageCodeProvider.GetLanguageCode() : Language;

        if (!string.IsNullOrWhiteSpace(language))
        {
            output.Attributes.Add("data-language", language);
        }
    }
}
