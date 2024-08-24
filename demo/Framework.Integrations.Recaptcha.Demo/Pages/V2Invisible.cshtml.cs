using System.Text.Json;
using Framework.BuildingBlocks.Constants;
using Framework.Integrations.Recaptcha.Contracts;
using Framework.Integrations.Recaptcha.V2;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Framework.Integrations.Recaptcha.Demo.Pages;

public sealed class V2InvisibleModel(IReCaptchaSiteVerifyV2 siteVerify) : PageModel
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

        Result = JsonSerializer.Serialize(response, PlatformJsonConstants.DefaultPrettyJsonOptions);
    }
}
