// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.V3;
using Framework.Kernel.BuildingBlocks;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Framework.Integrations.Recaptcha.Demo.Pages;

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

        var response = await siteVerify.Verify(request);

        Result = JsonSerializer.Serialize(response, FrameworkJsonConstants.DefaultPrettyJsonOptions);
    }
}
