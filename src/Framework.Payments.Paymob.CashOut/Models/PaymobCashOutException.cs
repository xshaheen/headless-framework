// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net;

namespace Framework.Payments.Paymob.CashOut.Models;

[Serializable]
public sealed class PaymobCashOutException(string? message, HttpStatusCode statusCode, string? body)
    : Exception(message)
{
    /// <summary>Gets the HTTP response status code.</summary>
    /// <value>An HTTP status code.</value>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Gets the HTTP response body if presented.</summary>
    /// <value>An HTTP body.</value>
    public string? Body { get; } = body;

    public static async Task ThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body;

        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
#pragma warning disable ERP022
        catch
        {
            body = null;
        }
#pragma warning restore ERP022

        var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        var message = $"Paymob Cash In - Http request failed with status code ({statusCode}).";

        throw new PaymobCashOutException(message, response.StatusCode, body);
    }
}
