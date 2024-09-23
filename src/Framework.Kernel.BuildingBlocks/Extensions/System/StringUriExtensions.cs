// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System;

public static class StringUriExtensions
{
    public static string ToUriComponents(this string url, UriFormat uriFormat = UriFormat.UriEscaped)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var uri = new Uri(url, UriKind.RelativeOrAbsolute);

        return uri.GetComponents(UriComponents.SerializationInfoString, uriFormat);
    }

    [return: NotNullIfNotNull(nameof(url))]
    public static string? RemoveQueryString(this string? url, out string? queryString)
    {
        if (url is not null)
        {
            var queryIndex = url.IndexOf('?', StringComparison.Ordinal);

            if (queryIndex >= 0)
            {
                queryString = url[queryIndex..];

                return url[..queryIndex];
            }
        }

        queryString = null;

        return url;
    }
}
