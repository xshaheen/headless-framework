// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Encryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>
/// A key-encryption-key provider under its own name, standing in for a key service when a test moves keys between
/// providers.
/// </summary>
public sealed class TestKeyEncryptionKeyProvider : IKeyEncryptionKeyProvider
{
    public const string ProviderName = "TestService";

    private static readonly byte[] _Key = [.. Enumerable.Repeat((byte)0x33, 32)];

    public string Name => ProviderName;

    public ValueTask<WrappedDataKey> WrapAsync(
        ReadOnlyMemory<byte> dataKey,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        var wrapped = new byte[12 + dataKey.Length + 16];
        RandomNumberGenerator.Fill(wrapped.AsSpan(0, 12));
        using var aes = new AesGcm(_Key, 16);
        aes.Encrypt(
            wrapped.AsSpan(0, 12),
            dataKey.Span,
            wrapped.AsSpan(12, dataKey.Length),
            wrapped.AsSpan(12 + dataKey.Length),
            context.KeyId.ToByteArray()
        );

        return ValueTask.FromResult(new WrappedDataKey { KekId = "test-v1", Ciphertext = wrapped });
    }

    public ValueTask<byte[]> UnwrapAsync(
        WrappedDataKey wrapped,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        var length = wrapped.Ciphertext.Length - 28;
        var key = new byte[length];
        using var aes = new AesGcm(_Key, 16);
        aes.Decrypt(
            wrapped.Ciphertext.AsSpan(0, 12),
            wrapped.Ciphertext.AsSpan(12, length),
            wrapped.Ciphertext.AsSpan(12 + length),
            key,
            context.KeyId.ToByteArray()
        );

        return ValueTask.FromResult(key);
    }

    /// <summary>Registers this provider as the key-encryption key of an encryption setup.</summary>
    public static IEncryptionProviderOptionsExtension AsExtension() => new Extension();

    private sealed class Extension : IEncryptionProviderOptionsExtension
    {
        public void AddServices(IServiceCollection services) =>
            services.TryAddSingleton<IKeyEncryptionKeyProvider, TestKeyEncryptionKeyProvider>();
    }
}
