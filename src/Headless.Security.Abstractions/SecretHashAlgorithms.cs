// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>The PHC identifiers of the secret-hashing algorithms the framework ships.</summary>
[PublicAPI]
public static class SecretHashAlgorithms
{
    /// <summary>
    /// Argon2id (RFC 9106). Encoded as <c>$argon2id$v=19$m=&lt;KiB&gt;,t=&lt;iterations&gt;,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
    /// Requires the <c>Headless.Security.Argon2</c> package.
    /// </summary>
    public const string Argon2id = "argon2id";

    /// <summary>
    /// PBKDF2 with HMAC-SHA256. Encoded as <c>$pbkdf2-sha256$i=&lt;iterations&gt;,l=&lt;hash length&gt;$&lt;salt&gt;$&lt;hash&gt;</c>.
    /// Built into <c>Headless.Security</c>.
    /// </summary>
    public const string Pbkdf2Sha256 = "pbkdf2-sha256";
}
