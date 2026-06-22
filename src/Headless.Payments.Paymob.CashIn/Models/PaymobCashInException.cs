// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Payments.Paymob.CashIn.Models;

/// <summary>
/// Represents an HTTP error returned by the Paymob Accept (CashIn) API.
/// </summary>
/// <remarks>
/// Thrown by broker and authenticator methods when the Paymob API responds with a non-success
/// HTTP status code. Inspect <c>StatusCode</c> and <c>Body</c> to diagnose the failure.
/// </remarks>
[Serializable]
public sealed class PaymobCashInException(string? message, HttpStatusCode statusCode, string? body) : Exception(message)
{
    /// <summary>Gets the HTTP response status code returned by Paymob.</summary>
    /// <value>An HTTP status code.</value>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Gets the raw HTTP response body, if one was present and readable.</summary>
    /// <value>The response body string, or <see langword="null"/> when absent or unreadable.</value>
    public string? Body { get; } = body;

    /// <summary>
    /// Reads the response body and throws a <c>PaymobCashInException</c> when the response
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
        string? readError = null;

        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            body = null;
            readError = ex.GetType().Name;
        }

        var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var message = readError is null
            ? $"Paymob Cash In - Http request failed with status code ({statusCode})."
            : $"Paymob Cash In - Http request failed with status code ({statusCode}). Body read failed: {readError}";

        throw new PaymobCashInException(message, response.StatusCode, body);
    }
}
