// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Headless.Serializer;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Demo.Pages;

internal sealed class ProviderSelectionModel(ICaptchaProvider captchaProvider) : PageModel
{
    public string? Result { get; set; }

    public string? SelectedProvider { get; set; }

    public async Task OnPostAsync(string token, string provider)
    {
        SelectedProvider = provider;

        // Resolve the verifier by the name it was registered under — the default Turnstile provider's canonical
        // key or the "recaptcha" named provider — and route verification to that backend.
        var verifier = captchaProvider.GetVerifier(provider);

        var request = new CaptchaVerifyRequest
        {
            Response = token,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        var result = await verifier.VerifyAsync(request, HttpContext.RequestAborted);

        Result = JsonSerializer.Serialize(result, JsonConstants.DefaultPrettyJsonOptions);
    }
}
