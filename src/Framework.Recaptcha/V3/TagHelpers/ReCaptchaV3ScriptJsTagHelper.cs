// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Framework.Recaptcha.Contracts;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha.V3.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3-js", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class ReCaptchaV3ScriptJsTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(ReCaptchaSetup.V3Name);

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ValidJsIdentifierRegex();

    public string? Action { get; set; }

    public string? Callback { get; set; }

    public bool Execute { get; set; } = true;

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

        // Validate Callback is a valid JS identifier to prevent XSS
        if (!string.IsNullOrWhiteSpace(Callback) && !ValidJsIdentifierRegex().IsMatch(Callback))
        {
            throw new InvalidOperationException(
                $"Callback '{Callback}' is not a valid JavaScript identifier. " +
                "Must start with a letter or underscore and contain only letters, digits, or underscores.");
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Encode Action to prevent XSS injection
        var encodedAction = string.IsNullOrWhiteSpace(Action)
            ? null
            : JavaScriptEncoder.Default.Encode(Action);

        var callbackParam = Execute ? "" : "callback";
        var actionOption = encodedAction is null ? "" : $",{{action:'{encodedAction}'}}";
        var thenClause = Execute ? $".then(function(token){{{Callback}(token)}})" : ".then(callback)";
        var autoExecute = Execute ? "grecaptcha.reExecute()" : "";

        var script = $"grecaptcha.ready(function(){{ grecaptcha.reExecute = function({callbackParam}){{grecaptcha.execute('{_options.SiteKey}'{actionOption}){thenClause}}};{autoExecute}}});";

        output.Content.SetHtmlContent(script);
    }
}
