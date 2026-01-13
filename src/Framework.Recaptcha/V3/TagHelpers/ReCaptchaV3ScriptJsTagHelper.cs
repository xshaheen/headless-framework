// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Framework.Recaptcha.V3.TagHelpers;

[PublicAPI]
[HtmlTargetElement("recaptcha-script-v3-js", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ReCaptchaV3ScriptJsTagHelper(IOptionsSnapshot<ReCaptchaOptions> optionsAccessor) : TagHelper
{
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(RecaptchaSetup.V3Name);

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

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        var script =
            "grecaptcha.ready(function(){ "
            + "grecaptcha.reExecute = function("
            + (Execute ? "" : "callback")
            + "){"
            + "grecaptcha.execute('"
            + _options.SiteKey
            + "'"
            + (string.IsNullOrWhiteSpace(Action) ? "" : ",{action:'" + Action + "'}")
            + ")"
            + (Execute ? ".then(function(token){" + Callback + "(token)})" : ".then(callback)")
            + "};"
            + (Execute ? "grecaptcha.reExecute()" : "")
            + "});";

        output.Content.SetHtmlContent(script);
    }
}
