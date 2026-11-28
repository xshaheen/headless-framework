// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Polly;
using Polly.Timeout;

namespace Headless.Payments.Paymob.CashIn.Internal;

/// <summary>Sends one Paymob CashIn request and classifies what happened into a result.</summary>
internal static class PaymobCashInHttp
{
    private const string _Prefix = "Paymob Cash In";

    /// <summary>Sends <paramref name="request"/> and classifies the response.</summary>
    /// <param name="httpClient">The client to send with.</param>
    /// <param name="request">The request to send.</param>
    /// <param name="classify">
    /// Reads a parsed 2xx body for a business outcome, such as a declined charge. <see langword="null"/>
    /// treats every parsed 2xx body as succeeded.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the call. Cancellation before the request is sent throws; once sent, it reports
    /// <see cref="PaymobCashInOutcome.Unknown"/>, because Paymob may already have applied the request.
    /// </param>
    public static async Task<PaymobCashInResult<T>> SendAsync<T>(
        HttpClient httpClient,
        HttpRequestMessage request,
        Func<T, PaymobCashInOutcome>? classify,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        // A circuit breaker or rate limiter rejects before the request reaches the network, so nothing was sent.
        catch (ExecutionRejectedException e) when (e is not TimeoutRejectedException)
        {
            return PaymobCashInResult.Rejected<T>(
                new($"{_Prefix} - the request was not sent: {e.GetType().Name}.", Exception: e)
            );
        }
#pragma warning disable CA1031, ERP022 // Any fault once the request may have left is an unknown outcome; letting it escape as an exception is the double-charge bug this result exists to prevent.
        catch (Exception e)
        {
            return PaymobCashInResult.Unknown<T>(
                new($"{_Prefix} - no response; the request may have been applied: {e.GetType().Name}.", Exception: e)
            );
        }
#pragma warning restore CA1031, ERP022

        using (response)
        {
            return await _ReadAsync(response, classify, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<PaymobCashInResult<T>> _ReadAsync<T>(
        HttpResponseMessage response,
        Func<T, PaymobCashInOutcome>? classify,
        CancellationToken cancellationToken
    )
    {
        var statusCode = response.StatusCode;
        var status = ((int)statusCode).ToString(CultureInfo.InvariantCulture);
        string? body = null;
        Exception? readFault = null;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031, ERP022 // The fault is carried in the result; the outcome already depends only on the status below.
        catch (Exception e)
        {
            readFault = e;
        }
#pragma warning restore CA1031, ERP022

        if (!response.IsSuccessStatusCode)
        {
            // A 4xx is Paymob refusing the request; anything else (5xx, an unfollowed redirect) leaves it open.
            var definite = (int)statusCode is >= 400 and < 500;
            var error = new PaymobCashInResultError(
                definite
                    ? $"{_Prefix} - Paymob rejected the request with status code ({status})."
                    : $"{_Prefix} - Paymob answered with status code ({status}); the request may have been applied.",
                statusCode,
                body,
                readFault
            );

            return definite ? PaymobCashInResult.Rejected<T>(error) : PaymobCashInResult.Unknown<T>(error);
        }

        T? value;

        try
        {
            value =
                readFault is null && !string.IsNullOrEmpty(body)
                    ? JsonSerializer.Deserialize<T>(body, CashInJsonOptions.JsonOptions)
                    : default;
        }
        catch (JsonException e)
        {
            readFault = e;
            value = default;
        }

        if (value is null)
        {
            return PaymobCashInResult.Unknown<T>(
                new(
                    $"{_Prefix} - Paymob accepted the request with status code ({status}) but its response was unreadable.",
                    statusCode,
                    body,
                    readFault
                )
            );
        }

        var outcome = classify?.Invoke(value) ?? PaymobCashInOutcome.Succeeded;

        return outcome switch
        {
            PaymobCashInOutcome.Succeeded => PaymobCashInResult.Succeeded(value),
            PaymobCashInOutcome.Rejected => PaymobCashInResult.Rejected(
                new PaymobCashInResultError($"{_Prefix} - Paymob reported a rejection.", statusCode, body),
                value
            ),
            _ => PaymobCashInResult.Unknown(
                new PaymobCashInResultError(
                    $"{_Prefix} - Paymob reported a status this client does not recognize.",
                    statusCode,
                    body
                ),
                value
            ),
        };
    }

    /// <summary>Turns a failed authentication into a rejection of the call that needed it, which was never sent.</summary>
    public static PaymobCashInResult<T> NotSent<T>(PaymobCashInResultError authenticationError)
    {
        return PaymobCashInResult.Rejected<T>(
            authenticationError with
            {
                Message =
                    $"{_Prefix} - authentication failed, so the request was not sent. {authenticationError.Message}",
            }
        );
    }
}
