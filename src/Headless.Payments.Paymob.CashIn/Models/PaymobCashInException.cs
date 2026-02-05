// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Payments.Paymob.CashIn.Models;

[Serializable]
public sealed class PaymobCashInException(string? message, HttpStatusCode statusCode, string? body) : Exception(message)
{
    /// <summary>Gets the HTTP response status code.</summary>
    /// <value>An HTTP status code.</value>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Gets the HTTP response body if presented.</summary>
    /// <value>An HTTP body.</value>
    public string? Body { get; } = body;

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
