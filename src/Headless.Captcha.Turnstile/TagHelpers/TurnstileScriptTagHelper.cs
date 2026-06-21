// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

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

        output.TagName = "";

        var baseUrl = _options.VerifyBaseUrl.TrimEnd('/');

        var query = (ExplicitRender, string.IsNullOrWhiteSpace(Onload)) switch
        {
            (true, true) => "?render=explicit",
            (true, false) => $"?render=explicit&onload={Uri.EscapeDataString(Onload!)}",
            (false, false) => $"?onload={Uri.EscapeDataString(Onload!)}",
            (false, true) => "",
        };

        var src = $"{baseUrl}/turnstile/v0/api.js{query}";

        var scriptAsync = ScriptAsync ? "async" : "";
        var scriptDefer = ScriptDefer ? "defer" : "";

        output.Content.SetHtmlContent($"<script {scriptAsync} {scriptDefer} src=\"{src}\"></script>");
    }
}
