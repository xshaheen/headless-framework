// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>APNs rejected a channel-management request.</summary>
[PublicAPI]
public sealed class ApnsRequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ApnsRequestException() { }

    /// <summary>Creates the exception with a message.</summary>
    public ApnsRequestException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ApnsRequestException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates the exception for an APNs answer.</summary>
    public ApnsRequestException(string message, System.Net.HttpStatusCode statusCode, string? reason, string? requestId)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
        RequestId = requestId;
    }

    /// <summary>The HTTP status APNs answered with.</summary>
    public System.Net.HttpStatusCode? StatusCode { get; }

    /// <summary>The APNs error code from the response body, or <see langword="null"/> when it carried none.</summary>
    public string? Reason { get; }

    /// <summary>The <c>apns-request-id</c> of the rejected request, to quote when troubleshooting with Apple.</summary>
    public string? RequestId { get; }
}
