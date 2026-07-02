// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Urls;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Emits the Cloudflare Turnstile client API script (<c>turnstile/v0/api.js</c>). The base URL comes from the
/// default Turnstile options (<see cref="CaptchaConstants.TurnstileProvider"/>); set <see cref="ExplicitRender"/>
/// to append <c>?render=explicit</c> for JavaScript-driven rendering.
/// </summary>
[PublicAPI]
[HtmlTargetElement("turnstile-script", TagStructure = TagStructure.WithoutEndTag)]
public sealed class TurnstileScriptTagHelper(IOptionsSnapshot<TurnstileOptions> optionsAccessor) : TagHelper
{
    private readonly TurnstileOptions _options = optionsAccessor.Get(CaptchaConstants.TurnstileProvider);

    public bool ScriptAsync { get; set; } = true;

    public bool ScriptDefer { get; set; } = true;

    /// <summary>When set, appends <c>?render=explicit</c> so the widget is rendered programmatically.</summary>
    public bool ExplicitRender { get; set; }

    /// <summary>Optional. The name of an <c>onload</c> callback invoked when the API script is ready.</summary>
    public string? Onload { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
            <script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script>
        */

        // The script helpers render the default provider, like the widget helper. A missing/empty SiteKey means no
        // default was registered (named-only), so fail consistently with the widget instead of half-rendering.
        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "Turnstile tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseTurnstile(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        var src = Url.Parse(_options.VerifyBaseUrl.TrimEnd('/') + "/turnstile/v0/api.js")
            .SetQueryParam("render", ExplicitRender ? "explicit" : null)
            .SetQueryParam("onload", string.IsNullOrWhiteSpace(Onload) ? null : Onload)
            .ToString();

        // Emit through the tag-helper output API so the framework HTML-encodes the attribute value, matching the
        // widget helper. Avoids raw SetHtmlContent string-concatenation of the URL into the <script> markup.
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
    }
}
