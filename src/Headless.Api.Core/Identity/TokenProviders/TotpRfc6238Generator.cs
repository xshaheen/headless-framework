// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Generates and validates RFC 6238 TOTP codes using HMAC-SHA1, HMAC-SHA256, or HMAC-SHA512.
/// </summary>
/// <param name="timeProvider">The time provider used to obtain the current UTC time for step calculation.</param>
public sealed class TotpRfc6238Generator(TimeProvider timeProvider)
{
    private static readonly UTF8Encoding _Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>Generates a 6-digit TOTP code for the current time step.</summary>
    /// <param name="securityToken">The HMAC key (user's security token bytes).</param>
    /// <param name="timestep">Duration of each time step (e.g. 3 minutes). Must be positive.</param>
    /// <param name="modifier">Optional UTF-8 modifier string appended to the time-step bytes before hashing.</param>
    /// <param name="hashMode">HMAC algorithm to use. Defaults to <see cref="TotpHashMode.Sha1"/>.</param>
    /// <returns>A 6-digit integer TOTP code (0–999999).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="securityToken"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestep"/> is not positive.</exception>
    public int GenerateCode(
        byte[] securityToken,
        TimeSpan timestep,
        string? modifier = null,
        TotpHashMode hashMode = TotpHashMode.Sha1
    )
    {
        Argument.IsNotNull(securityToken);
        Argument.IsPositive(timestep);

        var modifierBytes = modifier is not null ? _Encoding.GetBytes(modifier) : null;

        return _ComputeTotp(securityToken, _GetCurrentTimeStepNumber(timestep), modifierBytes, hashMode);
    }

    /// <summary>
    /// Validates a TOTP <paramref name="code"/> against the current time step, accepting codes
    /// within ±<paramref name="variance"/> steps of the current step.
    /// </summary>
    /// <param name="securityToken">The HMAC key (user's security token bytes).</param>
    /// <param name="code">The 6-digit TOTP code to validate.</param>
    /// <param name="timestep">Duration of each time step. Must be positive.</param>
    /// <param name="variance">
    /// Number of adjacent steps (before and after) to accept. Defaults to 2.
    /// Must be zero or greater.
    /// </param>
    /// <param name="modifier">Optional UTF-8 modifier string used during code generation.</param>
    /// <param name="hashMode">HMAC algorithm used during code generation.</param>
    /// <returns><see langword="true"/> if <paramref name="code"/> matches any step in the acceptance window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="securityToken"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timestep"/> is not positive, or <paramref name="variance"/> is negative.
    /// </exception>
    public bool ValidateCode(
        byte[] securityToken,
        int code,
        TimeSpan timestep,
        int variance = 2,
        string? modifier = null,
        TotpHashMode hashMode = TotpHashMode.Sha1
    )
    {
        Argument.IsNotNull(securityToken);
        Argument.IsPositive(timestep);
        Argument.IsPositiveOrZero(variance);

        var currentTimeStep = _GetCurrentTimeStepNumber(timestep);
        var modifierBytes = modifier is not null ? _Encoding.GetBytes(modifier) : null;

        for (var i = -variance; i <= variance; i++)
        {
            var step = (long)currentTimeStep + i;

            if (step < 0)
            {
                continue;
            }

            var computedTotp = _ComputeTotp(securityToken, (ulong)step, modifierBytes, hashMode);

            if (computedTotp == code)
            {
                return true;
            }
        }

        return false;
    }

    private ulong _GetCurrentTimeStepNumber(TimeSpan timestep)
    {
        var delta = timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch;

        return (ulong)(delta.Ticks / timestep.Ticks);
    }

    private static int _ComputeTotp(byte[] key, ulong timestepNumber, byte[]? modifierBytes, TotpHashMode hashMode)
    {
        // See https://tools.ietf.org/html/rfc4226
        // We can add an optional modifier
        Span<byte> timestepAsBytes = stackalloc byte[sizeof(long)];
        var bitSuccess = BitConverter.TryWriteBytes(
            timestepAsBytes,
            IPAddress.HostToNetworkOrder((long)timestepNumber)
        );

        Debug.Assert(bitSuccess);

        var modifierCombinedBytes = timestepAsBytes;

        if (modifierBytes is not null)
        {
            modifierCombinedBytes = _ApplyModifier(timestepAsBytes, modifierBytes);
        }

        const int mod = 1000000; // # of 0's = length of pin

        var hashSizeInBytes = hashMode switch
        {
            TotpHashMode.Sha256 => HMACSHA256.HashSizeInBytes,
            TotpHashMode.Sha512 => HMACSHA512.HashSizeInBytes,
            _ => HMACSHA1.HashSizeInBytes,
        };

        Span<byte> hash = stackalloc byte[hashSizeInBytes];

        var hashSuccess = _TryHashData(hashMode, key, modifierCombinedBytes, hash);

        Debug.Assert(hashSuccess);

        // Generate DT string
        var offset = hash[^1] & 0xf;
        Debug.Assert(offset + 4 < hash.Length);

        var binaryCode =
            ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);

        return binaryCode % mod;
    }

    private static bool _TryHashData(TotpHashMode hashMode, byte[] key, Span<byte> data, Span<byte> destination)
    {
        return hashMode switch
        {
            TotpHashMode.Sha256 => HMACSHA256.TryHashData(key, data, destination, out _),
            TotpHashMode.Sha512 => HMACSHA512.TryHashData(key, data, destination, out _),
#pragma warning disable CA5350 // Uses a weak cryptographic algorithm HMACSHA1
            _ => HMACSHA1.TryHashData(key, data, destination, out _),
#pragma warning restore CA5350
        };
    }

    private static byte[] _ApplyModifier(Span<byte> input, byte[] modifierBytes)
    {
        var combined = new byte[checked(input.Length + modifierBytes.Length)];

        input.CopyTo(combined);
        Buffer.BlockCopy(modifierBytes, 0, combined, input.Length, modifierBytes.Length);

        return combined;
    }
}
