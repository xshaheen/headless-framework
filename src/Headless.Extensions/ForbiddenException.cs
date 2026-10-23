// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless;

/// <summary>
/// An exception that signals the authenticated caller may not perform the request.
/// Maps to HTTP 403 Forbidden in the API exception handler, with the same body as a returned
/// <see cref="ForbiddenError"/>: the descriptor's description becomes the problem <c>detail</c>.
/// </summary>
[PublicAPI]
public sealed class ForbiddenException : Exception
{
    /// <summary>The error code applied when no explicit code is provided.</summary>
    public const string DefaultErrorCode = ApiResultErrorCodes.Default;

    /// <summary>Initializes a forbidden exception from a single <see cref="ErrorDescriptor"/>.</summary>
    /// <param name="error">The descriptor of the reason the request is refused.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ForbiddenException(ErrorDescriptor error)
        : base($"Forbidden: {error}")
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>Initializes a forbidden exception from a message and an optional error code.</summary>
    /// <param name="message">The human-readable reason the request is refused.</param>
    /// <param name="code">The error code associated with the reason; defaults to <see cref="DefaultErrorCode"/>.</param>
    public ForbiddenException([LocalizationRequired] string message, string code = DefaultErrorCode)
        : base($"Forbidden: {message}")
    {
        Error = new(code, message);
    }

    /// <summary>The error descriptor describing why the request is refused.</summary>
    public ErrorDescriptor Error { get; }
}
