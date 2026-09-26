// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>Loads the APNs provider certificate from its base64 PKCS#12 text.</summary>
internal static class ApnsCertificateLoader
{
    /// <summary>
    /// The key storage flags for this OS. Linux keeps the key in memory only, so it never reaches disk. Windows needs
    /// the default flags because SChannel cannot use an ephemeral key for TLS client authentication, so the key
    /// reaches the user key store. macOS refuses ephemeral keys and imports the key into a temporary keychain.
    /// </summary>
    internal static X509KeyStorageFlags KeyStorageFlags { get; } =
        OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet;

    /// <summary>Decodes and loads the certificate. The caller owns and disposes the result.</summary>
    /// <exception cref="FormatException"><paramref name="base64Pkcs12"/> is not base64.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The data is not PKCS#12, or <paramref name="password"/> does not open it.
    /// </exception>
    public static X509Certificate2 Load(string base64Pkcs12, string? password)
    {
        Argument.IsNotNullOrWhiteSpace(base64Pkcs12);

        var bytes = Convert.FromBase64String(base64Pkcs12.Trim());

        return X509CertificateLoader.LoadPkcs12(bytes, password, KeyStorageFlags);
    }

    /// <summary>
    /// Loads the certificate and checks what APNs needs from it: base64 PKCS#12 text that <paramref name="password"/>
    /// opens, a private key, and an expiry after <paramref name="now"/>. Shared by the options validator and the
    /// certificate reload, so both accept exactly the same certificates.
    /// </summary>
    /// <returns>
    /// The certificate, which the caller owns and disposes, or <see langword="null"/> when any check fails.
    /// </returns>
    /// <remarks>
    /// The <paramref name="errors"/> messages never contain the certificate text or the password, so they are safe to
    /// log and to put in a validation failure.
    /// </remarks>
    public static X509Certificate2? LoadValid(
        string base64Pkcs12,
        string? password,
        DateTimeOffset now,
        out IReadOnlyList<string> errors
    )
    {
        X509Certificate2 certificate;

        try
        {
            certificate = Load(base64Pkcs12, password);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            // FormatException: not base64. CryptographicException: not PKCS#12, or the password does not open it.
            errors =
            [
                "APNs Certificate must be the base64 text of a PKCS#12 (.p12) file that CertificatePassword opens.",
            ];

            return null;
        }

        var failures = new List<string>(2);

        if (!certificate.HasPrivateKey)
        {
            failures.Add("APNs Certificate has no private key. Export the certificate together with its private key.");
        }

        var expiresAt = GetExpiresAt(certificate);

        if (expiresAt <= now)
        {
            failures.Add(
                $"APNs Certificate expired at {expiresAt.ToString("u", CultureInfo.InvariantCulture)}. Renew it in the Apple Developer account."
            );
        }

        errors = failures;

        if (failures.Count == 0)
        {
            return certificate;
        }

        certificate.Dispose();

        return null;
    }

    /// <summary>The certificate's expiry as a UTC instant; <see cref="X509Certificate2.NotAfter"/> is local time.</summary>
    public static DateTimeOffset GetExpiresAt(X509Certificate2 certificate)
    {
        return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }
}
