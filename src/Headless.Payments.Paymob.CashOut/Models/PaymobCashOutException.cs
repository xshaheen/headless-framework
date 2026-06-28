// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// Represents an HTTP error returned by the Paymob CashOut API.
/// </summary>
/// <remarks>
/// Thrown by broker and authenticator methods when Paymob responds with a non-success HTTP
/// status code. Inspect <c>StatusCode</c> and <c>Body</c> to diagnose the failure.
/// </remarks>
[Serializable]
public sealed class PaymobCashOutException(string? message, HttpStatusCode statusCode, string? body)
    : Exception(message)
{
    /// <summary>Gets the HTTP response status code returned by Paymob.</summary>
    /// <value>An HTTP status code.</value>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Gets the raw HTTP response body, if one was present and readable.</summary>
    /// <value>The response body string, or <see langword="null"/> when absent or unreadable.</value>
    public string? Body { get; } = body;

    /// <summary>
    /// Reads the response body and throws a <c>PaymobCashOutException</c> when the response
    /// indicates failure. Returns without throwing on success responses.
    /// </summary>
    /// <param name="response">The HTTP response to inspect.</param>
    /// <param name="cancellationToken">Token to cancel the body-read operation.</param>
    public static async Task ThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable ERP022
        catch
        {
            body = null;
        }
#pragma warning restore ERP022

        var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var message = $"Paymob Cash Out - Http request failed with status code ({statusCode}).";

        throw new PaymobCashOutException(message, response.StatusCode, body);
    }
}
