// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Mints and caches ES256 APNs provider tokens per team and key identifier.
/// </summary>
internal sealed class ApnsTokenSource(TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan _RefreshAge = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan _MinimumRemintAge = TimeSpan.FromMinutes(20);

    private readonly ConcurrentDictionary<(string TeamId, string KeyId), KeyEntry> _entries = new();
    private readonly Lock _entriesLock = new();
    private volatile bool _disposed;

    /// <summary>Retrieves an active provider token, minting a new token when expired or missing.</summary>
    /// <param name="options">The APNs options containing credentials.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A valid <see cref="ApnsProviderToken"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// The options do not configure token authentication properties.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An existing entry for the team and key ID uses a different private key.
    /// </exception>
    public ValueTask<ApnsProviderToken> GetTokenAsync(ApnsOptions options, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(options);

        var entry = _GetEntry(options);
        var current = entry.Current;

        if (current is not null && !_IsStale(current))
        {
            return ValueTask.FromResult(current);
        }

        return _MintAsync(entry, rejectedGeneration: null, cancellationToken);
    }

    /// <summary>
    /// Invalidates an expired token generation and returns a refreshed token.
    /// </summary>
    /// <param name="options">The APNs options containing credentials.</param>
    /// <param name="generation">The token generation that failed authentication.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A refreshed <see cref="ApnsProviderToken"/> instance.</returns>
    /// <exception cref="ArgumentException">
    /// The options do not configure token authentication properties.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An existing entry for the team and key ID uses a different private key.
    /// </exception>
    public ValueTask<ApnsProviderToken> InvalidateAsync(
        ApnsOptions options,
        long generation,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(options);

        return _MintAsync(_GetEntry(options), generation, cancellationToken);
    }

    public void Dispose()
    {
        lock (_entriesLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Dispose();
            }

            _entries.Clear();
        }
    }

    private KeyEntry _GetEntry(ApnsOptions options)
    {
        Ensure.NotDisposed(_disposed, this);

        // Optional on the options because certificate mode leaves them unset; token mode requires all three.
        var teamId = Argument.IsNotNullOrWhiteSpace(options.TeamId);
        var keyId = Argument.IsNotNullOrWhiteSpace(options.KeyId);
        var privateKey = Argument.IsNotNullOrWhiteSpace(options.PrivateKey);
        var identity = (teamId, keyId);

        if (!_entries.TryGetValue(identity, out var entry))
        {
            // Entry creation is rare and imports a key, so it runs under a lock rather than GetOrAdd, whose
            // factory can run more than once and leak the losing ECDsa instance.
            lock (_entriesLock)
            {
                Ensure.NotDisposed(_disposed, this);

                if (!_entries.TryGetValue(identity, out entry))
                {
                    entry = new KeyEntry(teamId, keyId, privateKey);
                    _entries[identity] = entry;
                }
            }
        }

        if (!string.Equals(entry.PrivateKeyText, privateKey, StringComparison.Ordinal))
        {
            // Signing with whichever key loaded first would hide the misconfiguration until APNs rejects the token.
            throw new InvalidOperationException(
                $"APNs key id '{keyId}' for team '{teamId}' is already configured with a different private key. Option sets that share a team id and key id must use the same private key."
            );
        }

        return entry;
    }

    private async ValueTask<ApnsProviderToken> _MintAsync(
        KeyEntry entry,
        long? rejectedGeneration,
        CancellationToken cancellationToken
    )
    {
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Ensure.NotDisposed(_disposed, this);

            var current = entry.Current;

            if (current is not null && !_IsStale(current) && !_ShouldRemint(current, rejectedGeneration))
            {
                return current;
            }

            var minted = _Mint(entry, (current?.Generation ?? 0) + 1);
            entry.Current = minted;
            ApnsMetrics.RecordProviderTokenMinted();

            return minted;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private bool _IsStale(ApnsProviderToken token)
    {
        return timeProvider.GetUtcNow() - token.MintedAt >= _RefreshAge;
    }

    private bool _ShouldRemint(ApnsProviderToken current, long? rejectedGeneration)
    {
        return rejectedGeneration == current.Generation
            && timeProvider.GetUtcNow() - current.MintedAt >= _MinimumRemintAge;
    }

    private ApnsProviderToken _Mint(KeyEntry entry, long generation)
    {
        var now = timeProvider.GetUtcNow();

        var buffer = new ArrayBufferWriter<byte>(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("alg", "ES256");
            writer.WriteString("kid", entry.KeyId);
            writer.WriteEndObject();
        }

        var header = Base64Url.EncodeToString(buffer.WrittenSpan);
        buffer.ResetWrittenCount();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", entry.TeamId);
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        var signingInput = $"{header}.{Base64Url.EncodeToString(buffer.WrittenSpan)}";

        // ECDsa.SignData emits the fixed-width IEEE P1363 r||s form by default, which is what JWS ES256 requires.
        var signature = entry.Key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);

        return new ApnsProviderToken($"{signingInput}.{Base64Url.EncodeToString(signature)}", generation, now);
    }

    private sealed class KeyEntry : IDisposable
    {
        public KeyEntry(string teamId, string keyId, string privateKeyText)
        {
            TeamId = teamId;
            KeyId = keyId;
            PrivateKeyText = privateKeyText;
            Key = ECDsa.Create();

            try
            {
                Key.ImportFromPem(privateKeyText);
            }
            catch
            {
                Key.Dispose();

                throw;
            }
        }

        public string TeamId { get; }

        public string KeyId { get; }

        public string PrivateKeyText { get; }

        // Used only under Gate, since an ECDsa instance is not documented as safe for concurrent signing.
        public ECDsa Key { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        // Replaced whole, never mutated, so a lock-free reader sees either the old or the new token.
        public ApnsProviderToken? Current
        {
            get => Volatile.Read(ref field);
            set => Volatile.Write(ref field, value);
        }

        public void Dispose()
        {
            Key.Dispose();
            Gate.Dispose();
        }
    }
}

/// <summary>An APNs provider token (ES256 JWT) and the mint generation that identifies it.</summary>
/// <param name="Value">The signed JWT sent as the <c>authorization: bearer</c> value.</param>
/// <param name="Generation">
/// Increases with every mint for one key identity; a caller passes it back to invalidate exactly the token it used.
/// </param>
/// <param name="MintedAt">The instant written into the <c>iat</c> claim.</param>
internal sealed record ApnsProviderToken(string Value, long Generation, DateTimeOffset MintedAt);
