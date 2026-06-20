// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Exceptions;

/// <summary>
/// An exception that signals the current request lacks valid authentication credentials.
/// Maps to HTTP 401 Unauthorized in the API exception handler.
/// </summary>
[PublicAPI]
public sealed class UnauthorizedException : Exception
{
    /// <summary>The error code applied when no explicit code is provided.</summary>
    public const string DefaultErrorCode = "error";

    /// <summary>Initializes an unauthorized exception from a single <see cref="ErrorDescriptor"/>.</summary>
    /// <param name="error">The descriptor of the unauthorized condition.</param>
    public UnauthorizedException(ErrorDescriptor error)
        : base($"Unauthorized: {error}")
    {
        Error = error;
    }

    /// <summary>Initializes an unauthorized exception from a message and an optional error code.</summary>
    /// <param name="message">The human-readable unauthorized condition message.</param>
    /// <param name="code">The error code associated with the condition; defaults to <see cref="DefaultErrorCode"/>.</param>
    public UnauthorizedException([LocalizationRequired] string message, string code = DefaultErrorCode)
        : base($"Unauthorized: {message}")
    {
        Error = new(code, message);
    }

    /// <summary>The error descriptor describing the unauthorized condition.</summary>
    public ErrorDescriptor Error { get; }
}
