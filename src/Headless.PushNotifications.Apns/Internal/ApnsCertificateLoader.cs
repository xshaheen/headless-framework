// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>Loads and validates APNs provider certificates from Base64 PKCS#12 payloads.</summary>
internal static class ApnsCertificateLoader
{
    /// <summary>
    /// Gets platform-specific key storage flags for certificate importing.
    /// </summary>
    internal static X509KeyStorageFlags KeyStorageFlags { get; } =
        OperatingSystem.IsLinux() ? X509KeyStorageFlags.EphemeralKeySet : X509KeyStorageFlags.DefaultKeySet;

    /// <summary>Decodes and loads a PKCS#12 certificate from Base64 text.</summary>
    /// <param name="base64Pkcs12">The Base64-encoded PKCS#12 certificate data.</param>
    /// <param name="password">The password for the certificate file.</param>
    /// <returns>A loaded <see cref="X509Certificate2"/> instance.</returns>
    /// <exception cref="FormatException"><paramref name="base64Pkcs12"/> is not valid Base64.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The payload is not valid PKCS#12 or <paramref name="password"/> is incorrect.
    /// </exception>
    public static X509Certificate2 Load(string base64Pkcs12, string? password)
    {
        Argument.IsNotNullOrWhiteSpace(base64Pkcs12);

        var bytes = Convert.FromBase64String(base64Pkcs12.Trim());

        return X509CertificateLoader.LoadPkcs12(bytes, password, KeyStorageFlags);
    }

    /// <summary>
    /// Loads and validates a certificate for APNs client TLS authentication.
    /// </summary>
    /// <param name="base64Pkcs12">The Base64-encoded PKCS#12 data.</param>
    /// <param name="password">The certificate password.</param>
    /// <param name="now">The current timestamp for expiration verification.</param>
    /// <param name="errors">A list of validation error descriptions when loading fails.</param>
    /// <returns>
    /// The loaded <see cref="X509Certificate2"/> instance when valid; otherwise, <see langword="null"/>.
    /// </returns>
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
            // Catches invalid Base64 decoding or corrupt PKCS#12 payload and password mismatch.
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

    /// <summary>Gets the certificate expiration timestamp in UTC.</summary>
    /// <param name="certificate">The certificate to evaluate.</param>
    /// <returns>The expiration date and time in UTC.</returns>
    public static DateTimeOffset GetExpiresAt(X509Certificate2 certificate)
    {
        return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }
}
