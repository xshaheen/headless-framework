// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.V3;
using Headless.Serializer;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Pages;

internal sealed class V3Model(IReCaptchaSiteVerifyV3 siteVerify) : PageModel
{
    public string? Result { get; set; }

    public async Task OnPostAsync(string token)
    {
        var request = new ReCaptchaSiteVerifyRequest
        {
            Response = token,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        // Enforce success + action match (anti-replay) + score threshold server-side.
        var result = await siteVerify.VerifyAsync(request, expectedAction: "login", minimumScore: 0.5f);

        Result = JsonSerializer.Serialize(result, JsonConstants.DefaultPrettyJsonOptions);
    }
}
