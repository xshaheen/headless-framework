// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>URL-oriented extension methods for <see cref="string"/>.</summary>
public static class StringUriExtensions
{
    /// <summary>
    /// Parses <paramref name="url"/> as a URI and returns its canonical serialized components using the
    /// specified escaping format.
    /// </summary>
    /// <param name="url">The URL to normalize. May be absolute or relative.</param>
    /// <param name="uriFormat">Controls how reserved characters in the result are escaped. Defaults to <see cref="UriFormat.UriEscaped"/>.</param>
    /// <returns>
    /// The canonical serialized form of <paramref name="url"/>, or the original value unchanged if it is
    /// <see langword="null"/> or empty.
    /// </returns>
    /// <exception cref="UriFormatException">Thrown when <paramref name="url"/> is not a valid URI.</exception>
    public static string ToUriComponents(this string url, UriFormat uriFormat = UriFormat.UriEscaped)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        var uri = new Uri(url, UriKind.RelativeOrAbsolute);

        return uri.GetComponents(UriComponents.SerializationInfoString, uriFormat);
    }

    /// <summary>Splits the query string off a URL, returning the path portion and emitting the query separately.</summary>
    /// <param name="url">The URL to split.</param>
    /// <param name="queryString">
    /// When this method returns, the query string portion (including the leading <c>?</c>) if present;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <paramref name="url"/> without its query string, the original value if it has no query string, or
    /// <see langword="null"/> if <paramref name="url"/> is <see langword="null"/>.
    /// </returns>
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
