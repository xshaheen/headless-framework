// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Primitives;

namespace Headless;

/// <summary>
/// An exception that signals the current request lacks valid authentication credentials.
/// Maps to HTTP 401 Unauthorized in the API exception handler.
/// </summary>
/// <remarks>
/// Derive from this class to give the failure its own type that application code can catch by name. The API exception
/// handler maps every derived type to the same 401 body as <see cref="UnauthorizedException"/>, so the derived type needs
/// no exception handler of its own. Pass the descriptor and your own message through the protected constructor.
/// </remarks>
[PublicAPI]
public class UnauthorizedException : Exception
{
    /// <summary>The error code applied when no explicit code is provided.</summary>
    public const string DefaultErrorCode = ApiResultErrorCodes.Default;

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

    /// <summary>Initializes a derived exception with its own message.</summary>
    /// <param name="message">The exception message, for logs; it never reaches the response.</param>
    /// <param name="error">The descriptor written to the response.</param>
    /// <param name="innerException">The exception that caused this failure, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="error"/> is <see langword="null"/>.</exception>
    protected UnauthorizedException(string message, ErrorDescriptor error, Exception? innerException = null)
        : base(Argument.IsNotNull(message), innerException)
    {
        Error = Argument.IsNotNull(error);
    }

    /// <summary>The error descriptor describing the unauthorized condition.</summary>
    public ErrorDescriptor Error { get; }
}
