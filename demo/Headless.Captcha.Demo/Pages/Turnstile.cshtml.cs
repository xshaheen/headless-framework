// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Headless.Serializer;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Pages;

internal sealed class TurnstileModel(ITurnstileVerifier verifier) : PageModel
{
    public string? Result { get; set; }

    public async Task OnPostAsync(string token)
    {
        var request = new TurnstileVerifyRequest
        {
            Response = token,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        var result = await verifier.VerifyAsync(request);

        Result = JsonSerializer.Serialize(result, JsonConstants.DefaultPrettyJsonOptions);
    }
}
