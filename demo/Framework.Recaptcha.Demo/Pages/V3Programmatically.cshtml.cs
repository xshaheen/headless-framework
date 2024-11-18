// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.BuildingBlocks;
using Framework.Recaptcha.Contracts;
using Framework.Recaptcha.V3;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Framework.Recaptcha.Demo.Pages;

internal sealed class V3ProgrammaticallyModel(IReCaptchaSiteVerifyV3 siteVerify) : PageModel
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
