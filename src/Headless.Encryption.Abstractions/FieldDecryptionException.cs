// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Thrown when a field value cannot be decrypted. It never carries partial plaintext, and the value is never returned
/// as if it were plaintext.
/// </summary>
/// <param name="failure">Why the value was refused.</param>
/// <param name="message">The message that describes the failure.</param>
/// <param name="innerException">The cryptographic exception that caused it, or <see langword="null" />.</param>
[PublicAPI]
public sealed class FieldDecryptionException(
    FieldDecryptionFailure failure,
    string message,
    Exception? innerException = null
) : Exception(message, innerException)
{
    /// <summary>Gets why the value was refused.</summary>
    public FieldDecryptionFailure Failure { get; } = failure;
}
