// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V2;

/// <summary>
/// Verify requests with a user challenge. It has three types of challenges:
/// <list type="bullet">
///   <item>Checkbox: Validate requests with "I'm not a robot" checkbox challenge.</item>
///   <item>Invisible: Validate requests in the background.</item>
///  <item>Android: Validate requests in your android app.</item>
/// </list>
/// </summary>
public interface IReCaptchaSiteVerifyV2
{
    /// <summary>Validate a reCAPTCHA token against Google's siteverify endpoint.</summary>
    /// <param name="request">The verification request containing the reCAPTCHA response token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized siteverify response.</returns>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The response body could not be deserialized.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<ReCaptchaSiteVerifyV2Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ReCaptchaSiteVerifyV2(
    IOptionsMonitor<ReCaptchaOptions> optionsAccessor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV2> logger
) : IReCaptchaSiteVerifyV2
{
    private readonly HttpClient _client = clientFactory.CreateClient(SetupReCaptcha.V2Name);
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(SetupReCaptcha.V2Name);

    public async Task<ReCaptchaSiteVerifyV2Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var response = await ReCaptchaSiteVerifyClient
            .SendAsync<ReCaptchaSiteVerifyV2Response>(_client, _options, request, logger, cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            logger.LogReCaptchaFailure(response);
        }

        return response;
    }
}
