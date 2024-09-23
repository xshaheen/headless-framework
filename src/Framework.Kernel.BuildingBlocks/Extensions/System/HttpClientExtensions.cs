// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Net;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class HttpClientExtensions
{
    public static bool IsSuccessStatusCode(this HttpStatusCode code)
    {
        return IsSuccessStatusCode((int)code);
    }

    public static bool IsSuccessStatusCode(this int code)
    {
        return code is >= 200 and <= 299;
    }

    public static void EnsureSuccessStatusCode(this HttpStatusCode code)
    {
        ((int)code).EnsureSuccessStatusCode();
    }

    public static void EnsureSuccessStatusCode(this int code)
    {
        if (!code.IsSuccessStatusCode())
        {
            throw new HttpRequestException(
                $"The HTTP status code of the response was not success ({code.ToString(CultureInfo.InvariantCulture)})."
            );
        }
    }
}
