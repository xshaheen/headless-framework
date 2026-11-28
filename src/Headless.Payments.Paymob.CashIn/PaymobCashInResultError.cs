// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.Payments.Paymob.CashIn;

/// <summary>Describes a rejected or unknown Paymob CashIn outcome.</summary>
/// <param name="Message">A human-readable summary for logs. It never contains the response body.</param>
/// <param name="StatusCode">The HTTP status Paymob answered with, or <see langword="null"/> when no response arrived.</param>
/// <param name="Body">The raw response body, when one arrived and was readable. It may carry customer data.</param>
/// <param name="Exception">The transport, timeout, or parsing fault behind the outcome, when there was one.</param>
[PublicAPI]
public sealed record PaymobCashInResultError(
    string Message,
    HttpStatusCode? StatusCode = null,
    string? Body = null,
    Exception? Exception = null
);
