// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Headless.Recaptcha.Contracts;
using Headless.Recaptcha.V2;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Pages;

internal sealed class V2InvisibleModel(IReCaptchaSiteVerifyV2 siteVerify) : PageModel
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
