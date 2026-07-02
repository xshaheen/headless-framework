// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// Razor tag helper that renders an inline <c>&lt;script&gt;</c> block that wires up the
/// <c>grecaptcha.execute</c> call for reCAPTCHA v3. Use as
/// <c>&lt;recaptcha-script-v3-js /&gt;</c> after the API script rendered by
/// <c>&lt;recaptcha-script-v3 /&gt;</c>.
/// </summary>
/// <remarks>
/// When <c>Execute</c> is <see langword="true"/> (the default), the script immediately executes on
/// <c>grecaptcha.ready</c> and passes the token to the named <c>Callback</c> function. When
/// <c>Execute</c> is <see langword="false"/>, a <c>grecaptcha.reExecute</c> function is exposed for
/// manual invocation; the user-supplied callback is passed as an argument rather than referenced by name.
/// <c>Callback</c> is validated as a JavaScript identifier to prevent XSS injection.
/// </remarks>
[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3-js", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class ReCaptchaV3ScriptJsTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(CaptchaConstants.ReCaptchaV3Provider);

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled, 100)]
    private static partial Regex ValidJsIdentifierRegex { get; }

    /// <summary>
    /// The reCAPTCHA action name passed to <c>grecaptcha.execute</c>. Provide a meaningful value (for
    /// example <c>login</c> or <c>register</c>) to distinguish actions in the reCAPTCHA admin console and
    /// to enable action verification server-side.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// The name of the JavaScript function that receives the reCAPTCHA token. Must be a valid JavaScript
    /// identifier. When <see cref="Execute"/> is <see langword="true"/>, the function is called with the
    /// token immediately; when <see langword="false"/>, it is passed as the argument to
    /// <c>grecaptcha.reExecute</c>.
    /// </summary>
    /// <remarks>Validated against <c>^[a-zA-Z_][a-zA-Z0-9_]*$</c> to prevent XSS injection.</remarks>
    public string? Callback { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), <c>grecaptcha.execute</c> is called automatically inside
    /// <c>grecaptcha.ready</c>. When <see langword="false"/>, only the <c>grecaptcha.reExecute</c> helper
    /// is defined, allowing the caller to trigger execution programmatically.
    /// </summary>
    public bool Execute { get; set; } = true;

    /// <summary>
    /// Renders the inline reCAPTCHA v3 JavaScript block.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Callback</c> is set to a value that is not a valid JavaScript identifier.
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
