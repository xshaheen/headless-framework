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

        if (remoteIp is not null)
        {
            formData.Add(new("remoteip", remoteIp));
        }

        if (idempotencyKey is not null)
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
            if (logger?.IsEnabled(LogLevel.Information) is true)
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

        var wire = await JsonSerializer
            .DeserializeAsync(
                responseStream,
                TurnstileJsonSerializerContext.Default.TurnstileSiteVerifyResponse,
                cancellationToken
            )
            .ConfigureAwait(false);

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
