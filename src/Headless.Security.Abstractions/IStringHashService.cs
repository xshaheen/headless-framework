// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>
/// Creates deterministic, salted hashes for string values, intended for lookup and indexing (for example, blind
/// indexes over encrypted columns) rather than password storage.
/// </summary>
/// <remarks>
/// The hash is deterministic: the same value and salt always produce the same output, and the output carries no
/// embedded salt or parameters. This service is therefore <b>not suitable for password storage</b> — it has no
/// per-record random salt and no verification primitive. For passwords use a dedicated password hasher
/// (for example ASP.NET Core's <c>PasswordHasher&lt;T&gt;</c>). To compare a value against a stored hash, recompute
/// the hash with the same salt and options and compare the results.
/// </remarks>
[PublicAPI]
public interface IStringHashService
{
    /// <summary>Creates a hash for the specified value.</summary>
    /// <param name="value">The value to hash.</param>
    /// <param name="salt">
    /// An optional salt override. Uses the configured default salt when omitted, or an empty salt when no default is
    /// configured. An empty salt yields an unsalted (globally deterministic) hash — supply a salt when that matters.
    /// </param>
    /// <returns>The computed hash encoded as Base64.</returns>
    string Create(string value, string? salt = null);
}
