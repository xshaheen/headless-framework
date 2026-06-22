// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extensions for inspecting <see cref="HttpStatusCode"/> success ranges.</summary>
[PublicAPI]
public static class HttpStatusCodeExtensions
{
    /// <summary>Determines whether <paramref name="code"/> is in the success range (2xx).</summary>
    /// <param name="code">The HTTP status code to inspect.</param>
    /// <returns><see langword="true"/> when <paramref name="code"/> is between 200 and 299 (inclusive); otherwise <see langword="false"/>.</returns>
    public static bool IsSuccessStatusCode(this HttpStatusCode code)
    {
        return (int)code is >= 200 and <= 299;
    }

    /// <summary>Throws if <paramref name="code"/> is not in the success range (2xx).</summary>
    /// <param name="code">The HTTP status code to validate.</param>
    /// <exception cref="HttpRequestException">Thrown when <paramref name="code"/> is outside the 200-299 success range.</exception>
    public static void EnsureSuccessStatusCode(this HttpStatusCode code)
    {
        var codeNum = (int)code;

        if (codeNum is < 200 or > 299)
        {
            throw new HttpRequestException(
                $"The HTTP status code of the response was not success ({codeNum.ToString(CultureInfo.InvariantCulture)})."
            );
        }
    }
}
