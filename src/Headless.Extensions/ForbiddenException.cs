// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless;

/// <summary>
/// An exception that signals the authenticated caller may not perform the request.
/// Maps to HTTP 403 Forbidden in the API exception handler, with the same body as a returned
/// <see cref="ForbiddenError"/>: the descriptor's description becomes the problem <c>detail</c>.
/// </summary>
/// <remarks>
/// Derive from this class to give the failure its own type that application code can catch by name. The API exception
/// handler maps every derived type to the same 403 body as <see cref="ForbiddenException"/>, so the derived type needs
/// no exception handler of its own. Pass the descriptor and your own message through the protected constructor.
/// </remarks>
[PublicAPI]
public class ForbiddenException : Exception
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

    /// <summary>Initializes a derived exception with its own message.</summary>
    /// <param name="message">The exception message, for logs; it never reaches the response.</param>
    /// <param name="error">The descriptor written to the response.</param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="error"/> is <see langword="null"/>.</exception>
    protected ForbiddenException(string message, ErrorDescriptor error, Exception? innerException = null)
        : base(Argument.IsNotNull(message), innerException)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The error descriptor describing why the request is refused.</summary>
    public ErrorDescriptor Error { get; }
}
