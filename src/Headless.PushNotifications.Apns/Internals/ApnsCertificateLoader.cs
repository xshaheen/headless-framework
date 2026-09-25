// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    /// <summary>The certificate's expiry as a UTC instant; <see cref="X509Certificate2.NotAfter"/> is local time.</summary>
    public static DateTimeOffset GetExpiresAt(X509Certificate2 certificate)
    {
        return new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }
}
