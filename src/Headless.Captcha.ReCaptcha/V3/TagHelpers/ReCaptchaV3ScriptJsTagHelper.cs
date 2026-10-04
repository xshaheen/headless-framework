// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// Renders an inline <c>&lt;script&gt;</c> element configuring client execution for reCAPTCHA v3.
/// </summary>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3-js", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class ReCaptchaV3ScriptJsTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV3Provider);

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled, 100)]
    private static partial Regex ValidJsIdentifierRegex { get; }

    /// <summary>
    /// Gets or sets the action name passed to <c>grecaptcha.execute</c>.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Gets or sets the name of the JavaScript function that receives the reCAPTCHA token.
    /// </summary>
    public string? Callback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to execute reCAPTCHA automatically on ready.
    /// </summary>
    public bool Execute { get; set; } = true;

    /// <summary>
    /// Renders the inline reCAPTCHA v3 script element.
    /// </summary>
    /// <param name="context">Contains information associated with the current HTML tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    /// <exception cref="InvalidOperationException">
    /// The reCAPTCHA v3 site key is not configured, or <see cref="Callback"/> is not a valid JavaScript identifier.
    /// </exception>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        /*
        myCallback is a user-defined method name or `(function(t){alert(t)})` when Execute = true
        grecaptcha.ready(function () {
            grecaptcha.reExecute = function () {
                grecaptcha.execute('6LccrsMUAAAAANSAh_MCplqdS9AJVPihyzmbPqWa', {
                    action: 'login'
                }).then(function (token) {
                    myCallback(token)
                })
            };
            grecaptcha.reExecute()
        });

        myCallback is a user-defined function when Execute = false
        grecaptcha.ready(function () {
            grecaptcha.reExecute = function (callback) {
                grecaptcha.execute('6LccrsMUAAAAANSAh_MCplqdS9AJVPihyzmbPqWa', {
                    action: 'login'
                }).then(myCallback)
            };
        });
         */

        if (string.IsNullOrWhiteSpace(_options.SiteKey))
        {
            throw new InvalidOperationException(
                "reCAPTCHA v3 tag helpers render the default provider; register it with "
                    + "AddHeadlessCaptcha(b => b.UseReCaptchaV3(...)) — a named-only registration is not rendered by tag helpers."
            );
        }

        // Validate Callback is a valid JS identifier to prevent XSS
        if (!string.IsNullOrWhiteSpace(Callback) && !ValidJsIdentifierRegex.IsMatch(Callback))
        {
            throw new InvalidOperationException(
                $"Callback '{Callback}' is not a valid JavaScript identifier. "
                    + "Must start with a letter or underscore and contain only letters, digits, or underscores."
            );
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        // SiteKey is config-controlled, but JS-encode it (and Action) for consistency and defense-in-depth against XSS.
        var encodedSiteKey = JavaScriptEncoder.Default.Encode(_options.SiteKey);
        var encodedAction = string.IsNullOrWhiteSpace(Action) ? null : JavaScriptEncoder.Default.Encode(Action);

        var callbackParam = Execute ? "" : "callback";
        var actionOption = encodedAction is null ? "" : $",{{action:'{encodedAction}'}}";
        var thenClause = Execute ? $".then(function(token){{{Callback}(token)}})" : ".then(callback)";
        var autoExecute = Execute ? "grecaptcha.reExecute()" : "";

        var script =
            $"grecaptcha.ready(function(){{ grecaptcha.reExecute = function({callbackParam}){{grecaptcha.execute('{encodedSiteKey}'{actionOption}){thenClause}}};{autoExecute}}});";

        output.Content.SetHtmlContent(script);
    }
}
