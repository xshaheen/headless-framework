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

        // The provider name arrives from untrusted form input, so resolve with the non-throwing GetVerifierOrNull and
        // short-circuit on an unknown name instead of letting GetVerifier throw a 500. (GetVerifierOrNull still
        // rejects an empty name, so guard that first.)
        var verifier = string.IsNullOrWhiteSpace(provider) ? null : captchaProvider.GetVerifierOrNull(provider);

        if (verifier is null)
        {
            Result = $"Unknown or unsupported captcha provider '{provider}'.";

            return;
        }

        var request = new CaptchaVerifyRequest
        {
            Response = token,
            RemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        var result = await verifier.VerifyAsync(request, HttpContext.RequestAborted);

        Result = JsonSerializer.Serialize(result, JsonConstants.DefaultPrettyJsonOptions);
    }
}
