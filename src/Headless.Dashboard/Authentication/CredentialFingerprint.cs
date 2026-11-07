// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Headless.Dashboard;

/// <summary>
/// Ties a session issued for a Basic or API-key credential to that credential, so changing the configured password
/// or key ends every session issued under the old one at its next request.
/// </summary>
/// <remarks>
/// The fingerprint is a SHA-256 hash of the mode and the configured credential, protected by Data Protection under a
/// purpose of its own and the dashboard's authentication name. The session carries it as a claim, and every cookie
/// validation compares it with a hash of the credential configured now: an in-memory comparison, so it needs no
/// revalidation interval and no host callback. Data Protection already protects the whole session cookie; the extra
/// purpose keeps the hash unreadable to any other protector over the same key ring, and a host that rotates or loses
/// its key ring ends these sessions as it ends the cookie itself.
/// </remarks>
internal static class CredentialFingerprint
{
    /// <summary>The claim type that carries the fingerprint in a credential session.</summary>
    internal const string ClaimType = "headless.dashboard.credential";

    private const string _Purpose = "Headless.Dashboard.CredentialFingerprint";

    /// <summary>
    /// Creates the fingerprint of the credential <paramref name="auth"/> configures, or <see langword="null"/> when its
    /// mode signs in with no credential of the dashboard's own.
    /// </summary>
    internal static string? Create(IDataProtectionProvider provider, string authenticationName, AuthConfig auth)
    {
        var hash = _Hash(auth);
        if (hash is null)
        {
            return null;
        }

        return Convert.ToBase64String(_Protector(provider, authenticationName).Protect(hash));
    }

    /// <summary>
    /// Whether the session <paramref name="principal"/> still matches the credential <paramref name="auth"/> configures:
    /// under Basic or API key, it must carry the fingerprint of the current credential; under any other mode, it must
    /// carry none, so a session issued for a credential does not outlive a switch to another mode.
    /// </summary>
    internal static bool IsCurrent(
        IDataProtectionProvider provider,
        string authenticationName,
        AuthConfig auth,
        ClaimsPrincipal principal
    )
    {
        var fingerprint = principal.FindFirst(ClaimType)?.Value;
        var expected = _Hash(auth);

        if (expected is null)
        {
            return fingerprint is null;
        }

        if (string.IsNullOrEmpty(fingerprint))
        {
            return false;
        }

        try
        {
            var actual = _Protector(provider, authenticationName).Unprotect(Convert.FromBase64String(fingerprint));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            // A fingerprint protected under a key the ring no longer holds, or a malformed one, matches no credential.
            return false;
        }
    }

    private static byte[]? _Hash(AuthConfig auth)
    {
        var credential = auth.Mode switch
        {
            AuthMode.Basic => auth.BasicCredentials,
            AuthMode.ApiKey => auth.ApiKey,
            _ => null,
        };

        if (string.IsNullOrEmpty(credential))
        {
            return null;
        }

        // The mode is part of the hash, so a Basic credential and an API key with the same text never match.
        return SHA256.HashData(Encoding.UTF8.GetBytes(auth.Mode.ToString() + ":" + credential));
    }

    private static IDataProtector _Protector(IDataProtectionProvider provider, string authenticationName)
    {
        return provider.CreateProtector(_Purpose, authenticationName);
    }
}
