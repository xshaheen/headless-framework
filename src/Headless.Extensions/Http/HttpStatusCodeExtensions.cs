// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class HttpStatusCodeExtensions
{
    public static bool IsSuccessStatusCode(this HttpStatusCode code)
    {
        return (int)code is >= 200 and <= 299;
    }

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
