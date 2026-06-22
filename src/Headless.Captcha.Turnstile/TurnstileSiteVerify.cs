// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Captcha;

/// <summary>
/// <see cref="ITurnstileVerifier"/> over Cloudflare's <c>turnstile/v0/siteverify</c> endpoint. Posts the
/// form-encoded secret/response (plus optional remote IP and idempotency key) and maps the wire response onto
/// <see cref="TurnstileVerifyResult"/>. Registered per slot, so it resolves its named options and HTTP client by
/// the registration name.
/// </summary>
internal sealed class TurnstileSiteVerify(
    string name,
    IOptionsMonitor<TurnstileOptions> optionsMonitor,
    IHttpClientFactory clientFactory,
    ILogger<TurnstileSiteVerify>? logger
) : ITurnstileVerifier
{
    private static readonly Uri _SiteVerifyUri = new("turnstile/v0/siteverify", UriKind.Relative);

    public Task<TurnstileVerifyResult> VerifyAsync(
        TurnstileVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return _VerifyAsync(request.Response, request.RemoteIp, request.IdempotencyKey, cancellationToken);
    }

    /// <summary>
    /// Verifies a CAPTCHA token through the shared <see cref="ICaptchaVerifier"/> contract, returning the common
    /// pass/fail result.
    /// </summary>
    /// <remarks>
    /// When <paramref name="request"/> is a <see cref="TurnstileVerifyRequest"/> the idempotency key is forwarded
    /// to Cloudflare's siteverify endpoint. A plain <see cref="CaptchaVerifyRequest"/> carries no idempotency key,
    /// so the field is silently omitted — no error is raised.
    /// </remarks>
    async Task<CaptchaVerifyResult> ICaptchaVerifier.VerifyAsync(
        CaptchaVerifyRequest request,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(request);

        var idempotencyKey = (request as TurnstileVerifyRequest)?.IdempotencyKey;

        return await _VerifyAsync(request.Response, request.RemoteIp, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TurnstileVerifyResult> _VerifyAsync(
        string response,
        string? remoteIp,
        string? idempotencyKey,
        CancellationToken cancellationToken
    )
    {
        var options = optionsMonitor.Get(name);

        List<KeyValuePair<string, string>> formData = [new("secret", options.SiteSecret), new("response", response)];

        if (!string.IsNullOrEmpty(remoteIp))
        {
            formData.Add(new("remoteip", remoteIp));
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            formData.Add(new("idempotency_key", idempotencyKey));
        }

        using var content = new FormUrlEncodedContent(formData);
        var client = clientFactory.CreateClient(name);

        using var httpResponseMessage = await client
            .PostAsync(_SiteVerifyUri, content, cancellationToken)
            .ConfigureAwait(false);

        if (!httpResponseMessage.IsSuccessStatusCode)
        {
            if (logger?.IsEnabled(LogLevel.Warning) is true)
            {
                var responseBody = await httpResponseMessage
                    .Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                logger.LogTurnstileHttpFailure(httpResponseMessage.StatusCode, responseBody);
            }

            httpResponseMessage.EnsureSuccessStatusCode();
        }

        await using var responseStream = await httpResponseMessage
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        TurnstileSiteVerifyResponse? wire;

        try
        {
            wire = await JsonSerializer
                .DeserializeAsync(
                    responseStream,
                    TurnstileJsonSerializerContext.Default.TurnstileSiteVerifyResponse,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to deserialize Turnstile siteverify response.", ex);
        }

        if (wire is null)
        {
            throw new InvalidOperationException(
                "Failed to deserialize Turnstile siteverify response. Response was null."
            );
        }

        if (!wire.Success)
        {
            logger?.LogTurnstileValidationFailure(wire.ErrorCodes);
        }

        return new TurnstileVerifyResult
        {
            Success = wire.Success,
            ChallengeTimestamp = wire.ChallengeTimestamp,
            HostName = wire.HostName,
            Action = wire.Action,
            ErrorCodes = wire.ErrorCodes,
            CData = wire.CData,
            Metadata = wire.Metadata,
        };
    }
}
