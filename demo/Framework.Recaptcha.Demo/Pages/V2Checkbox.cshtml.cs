// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.V2;
using Framework.Serializer;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Framework.Recaptcha.Demo.Pages;

internal sealed class V2CheckboxModel(IReCaptchaSiteVerifyV2 siteVerify) : PageModel
{
    public string? Result { get; set; }

    public async Task OnPostAsync(string token)
    {
        var request = new ReCaptchaSiteVerifyRequest
        {
            Response = token,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        var response = await siteVerify.VerifyAsync(request);

        Result = JsonSerializer.Serialize(response, JsonConstants.DefaultPrettyJsonOptions);
    }
}
