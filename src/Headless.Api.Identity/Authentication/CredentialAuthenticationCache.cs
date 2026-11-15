// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace Headless.Api.Identity;

/// <summary>
/// Remembers the principal of a successful Basic or API-key authentication for a short, absolute lifetime, so a client
/// that repeats the same credential skips the password hash and the store round trips until the entry expires.
/// </summary>
/// <remarks>
/// <para>
/// The cache never holds or is keyed by a secret: the key is an HMAC-SHA256 of the scheme and the credential under a
/// random key drawn when the process starts, so a memory dump shows no credential and no key outlives a restart.
/// Only successes are stored; a failed credential always runs the full path, so the cache answers no guess and never
/// bypasses Identity's failed-attempt counting.
/// </para>
/// <para>
/// Like <c>UserAgentParser</c>, it owns private <see cref="MemoryCache"/> instances rather than the host's shared
/// <c>IMemoryCache</c>: entries derive from request credentials, must not compete for the application's cache budget,
/// and never cross a process boundary. Each scheme gets its own size-bounded cache, created on its first use with that
/// scheme's <c>CredentialCacheMaxEntries</c>.
/// </para>
/// </remarks>
internal sealed class CredentialAuthenticationCache : IDisposable
{
    /// <summary>Longest lifetime a scheme may configure; a revoked credential keeps working for up to this long.</summary>
    internal static readonly TimeSpan MaxDuration = TimeSpan.FromHours(1);

    private const int _StackallocThreshold = 256;

    private readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, MemoryCache> _schemes = new(StringComparer.Ordinal);
    private readonly ISystemClock _clock;

    public CredentialAuthenticationCache(TimeProvider timeProvider)
    {
        _clock = new TimeProviderClock(timeProvider);
    }

    /// <summary>Whether <paramref name="options"/> turn the cache on.</summary>
    public static bool IsEnabled(ICredentialCacheOptions options) => options.CredentialCacheDuration > TimeSpan.Zero;

    /// <summary>Derives the cache key for <paramref name="credential"/> presented to <paramref name="scheme"/>.</summary>
    public string ComputeKey(string scheme, string credential)
    {
        // scheme NUL credential: a scheme name never contains NUL, so two (scheme, credential) pairs never share bytes.
        var length = Encoding.UTF8.GetByteCount(scheme) + 1 + Encoding.UTF8.GetByteCount(credential);
        var rented = length > _StackallocThreshold ? ArrayPool<byte>.Shared.Rent(length) : null;
        var buffer = rented is null ? stackalloc byte[_StackallocThreshold] : rented;

        try
        {
            var written = Encoding.UTF8.GetBytes(scheme, buffer);
            buffer[written++] = 0;
            written += Encoding.UTF8.GetBytes(credential, buffer[written..]);

            Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
            HMACSHA256.HashData(_hmacKey, buffer[..written], hash);

            return Convert.ToBase64String(hash);
        }
        finally
        {
            // The buffer held the raw credential; wipe it before the memory is reused.
            buffer.Clear();

            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Returns a private copy of the principal cached under <paramref name="key"/>, or <see langword="null"/> when the
    /// scheme has no live entry for it.
    /// </summary>
    public ClaimsPrincipal? TryGet(string scheme, string key)
    {
        return _schemes.TryGetValue(scheme, out var cache) && cache.TryGetValue<ClaimsPrincipal>(key, out var principal)
            ? _Copy(principal!)
            : null;
    }

    /// <summary>Caches a copy of <paramref name="principal"/> under <paramref name="key"/> for the scheme's lifetime.</summary>
    public void Set(string scheme, string key, ClaimsPrincipal principal, ICredentialCacheOptions options)
    {
        var cache = _schemes.GetOrAdd(
            scheme,
            static (_, state) =>
                new MemoryCache(new MemoryCacheOptions { SizeLimit = state.MaxEntries, Clock = state.Clock }),
            (MaxEntries: options.CredentialCacheMaxEntries, Clock: _clock)
        );

        // Store a copy: the caller hands the original to its request, which may add claims to it.
        cache.Set(
            key,
            _Copy(principal),
            new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(options.CredentialCacheDuration!.Value)
        );
    }

    public void Dispose()
    {
        foreach (var cache in _schemes.Values)
        {
            cache.Dispose();
        }
    }

    // ClaimsPrincipal.Clone() shares the identities, so a claim one request adds would reach every later hit; clone each
    // identity (and with it each claim) instead.
    private static ClaimsPrincipal _Copy(ClaimsPrincipal principal)
    {
        return new ClaimsPrincipal(principal.Identities.Select(static identity => identity.Clone()));
    }

    private sealed class TimeProviderClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }
}
