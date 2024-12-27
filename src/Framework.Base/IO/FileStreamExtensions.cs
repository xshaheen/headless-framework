// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Cysharp.Text;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

[PublicAPI]
public static class FileStreamExtensions
{
    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 is used for file integrity check."
    )]
    public static async Task<string> CalculateMd5Async(
        this Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        using var md5 = MD5.Create();
        var data = await md5.ComputeHashAsync(stream, cancellationToken);

        var sb = ZString.CreateStringBuilder();

        foreach (var d in data)
        {
            sb.Append(d.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
