// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.ReCaptcha.V3;

/// <summary>
/// reCAPTCHA v3 returns a score for each request without user friction. The score is based
/// on interactions with your site and enables you to take an appropriate action for your site.
/// </summary>
public interface IReCaptchaSiteVerifyV3
{
    /// <summary>Validate a reCAPTCHA token against Google's siteverify endpoint.</summary>
    /// <param name="request">The verification request containing the reCAPTCHA response token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized siteverify response. The caller is responsible for checking <c>Success</c>, <c>Action</c>, and <c>Score</c>.</returns>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The response body could not be deserialized.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<ReCaptchaSiteVerifyV3Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Validate a token and enforce the v3 security checks server-side: success, the action must match
    /// <paramref name="expectedAction"/> (preventing cross-action token replay), and the score must be at
    /// or above <paramref name="minimumScore"/>. A missing score is treated as a failure (fail-closed).
    /// </summary>
    /// <param name="request">The verification request containing the reCAPTCHA response token.</param>
    /// <param name="expectedAction">The action name expected for this request (must match <c>Response.Action</c>).</param>
    /// <param name="minimumScore">The minimum acceptable score, inclusive (e.g. <c>0.5f</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result describing whether the token is valid and, if not, why.</returns>
    /// <exception cref="ArgumentException"><paramref name="expectedAction"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="HttpRequestException">The HTTP response is unsuccessful.</exception>
    /// <exception cref="InvalidOperationException">The response body could not be deserialized.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    Task<ReCaptchaSiteVerifyV3Result> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        string expectedAction,
        float minimumScore,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ReCaptchaSiteVerifyV3(
    IOptionsMonitor<ReCaptchaOptions> optionsAccessor,
    IHttpClientFactory clientFactory,
    ILogger<ReCaptchaSiteVerifyV3> logger
) : IReCaptchaSiteVerifyV3
{
    private readonly HttpClient _client = clientFactory.CreateClient(SetupReCaptcha.V3Name);
    private readonly ReCaptchaOptions _options = optionsAccessor.Get(SetupReCaptcha.V3Name);

    public async Task<ReCaptchaSiteVerifyV3Response> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var response = await ReCaptchaSiteVerifyClient
            .SendAsync<ReCaptchaSiteVerifyV3Response>(_client, _options, request, logger, cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            logger.LogReCaptchaFailure(response);
        }

        return response;
    }

    public async Task<ReCaptchaSiteVerifyV3Result> VerifyAsync(
        ReCaptchaSiteVerifyRequest request,
        string expectedAction,
        float minimumScore,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(expectedAction);

        var response = await VerifyAsync(request, cancellationToken).ConfigureAwait(false);

        ReCaptchaV3VerificationFailureReason? failureReason = response switch
        {
            { Success: false } => ReCaptchaV3VerificationFailureReason.Unsuccessful,
            _ when !string.Equals(response.Action, expectedAction, StringComparison.Ordinal) =>
                ReCaptchaV3VerificationFailureReason.ActionMismatch,
            // Fail-closed: a missing score never passes the threshold.
            _ when response.Score is not { } score || score < minimumScore =>
                ReCaptchaV3VerificationFailureReason.ScoreBelowThreshold,
            _ => null,
        };

        return new ReCaptchaSiteVerifyV3Result
        {
            IsValid = failureReason is null,
            Response = response,
            FailureReason = failureReason,
        };
    }
}
