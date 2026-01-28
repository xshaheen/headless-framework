// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.Api.Identity.TokenProviders;

public sealed class TotpRfc6238Generator(TimeProvider timeProvider)
{
    private static readonly UTF8Encoding _Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public int GenerateCode(byte[] securityToken, TimeSpan timestep, string? modifier = null)
    {
        Argument.IsNotNull(securityToken);
        Argument.IsPositive(timestep);

        var modifierBytes = modifier is not null ? _Encoding.GetBytes(modifier) : null;

        return _ComputeTotp(securityToken, _GetCurrentTimeStepNumber(timestep), modifierBytes);
    }

    public bool ValidateCode(
        byte[] securityToken,
        int code,
        TimeSpan timestep,
        int variance = 2,
        string? modifier = null
    )
    {
        Argument.IsNotNull(securityToken);
        Argument.IsPositive(timestep);
        Argument.IsPositiveOrZero(variance);

        var currentTimeStep = _GetCurrentTimeStepNumber(timestep);
        var modifierBytes = modifier is not null ? _Encoding.GetBytes(modifier) : null;

        for (var i = -variance; i <= variance; i++)
        {
            var computedTotp = _ComputeTotp(securityToken, (ulong)((long)currentTimeStep + i), modifierBytes);

            if (computedTotp == code)
            {
                return true;
            }
        }

        return false; // No match
    }

    private ulong _GetCurrentTimeStepNumber(TimeSpan timestep)
    {
        var delta = timeProvider.GetUtcNow() - DateTimeOffset.UnixEpoch;

        return (ulong)(delta.Ticks / timestep.Ticks);
    }

    private static int _ComputeTotp(byte[] key, ulong timestepNumber, byte[]? modifierBytes)
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

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // Uses a weak cryptographic algorithm HMACSHA1
        var hashSuccess = HMACSHA1.TryHashData(key, modifierCombinedBytes, hash, out var written);
#pragma warning restore CA5350

        Debug.Assert(hashSuccess);
        Debug.Assert(written == hash.Length);

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

    private static byte[] _ApplyModifier(Span<byte> input, byte[] modifierBytes)
    {
        var combined = new byte[checked(input.Length + modifierBytes.Length)];

        input.CopyTo(combined);
        Buffer.BlockCopy(modifierBytes, 0, combined, input.Length, modifierBytes.Length);

        return combined;
    }
}
