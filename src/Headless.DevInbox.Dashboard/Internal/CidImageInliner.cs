// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Emails;

namespace Headless.DevInbox.Dashboard.Internal;

/// <summary>
/// Rewrites the <c>cid:</c> image references of a captured email's HTML into <c>data:</c> URIs of its kept image
/// attachments, so the sandboxed preview, which loads only <c>data:</c> images, shows them.
/// </summary>
/// <remarks>
/// A capture carries no Content-ID, so a reference matches the attachment whose file name equals it, or equals the
/// part before an <c>@</c> (the common <c>name@domain</c> form), ignoring case. Only kept raster images inline: an SVG
/// would carry script into the document. An unmatched reference stays as it is and shows as a missing image.
/// </remarks>
internal static partial class CidImageInliner
{
    /// <summary>
    /// The most characters of <c>data:</c> URIs one preview generates. One email that repeats a large image many times
    /// would otherwise build one full copy per reference; references past the budget stay unresolved.
    /// </summary>
    public const int MaxInlineLength = 4 * 1024 * 1024;

    public static string Inline(string html, IReadOnlyList<CapturedEmailAttachment> attachments)
    {
        if (!html.Contains("cid:", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var images = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var remaining = MaxInlineLength;

        return _CidReference.Replace(
            html,
            match =>
            {
                var token = match.Groups["token"].Value;

                if (!images.TryGetValue(token, out var uri))
                {
                    uri = _DataUri(token, attachments);
                    images[token] = uri;
                }

                if (uri is null || uri.Length > remaining)
                {
                    return match.Value;
                }

                remaining -= uri.Length;

                return match.Groups["prefix"].Value + uri;
            }
        );
    }

    private static string? _DataUri(string token, IReadOnlyList<CapturedEmailAttachment> attachments)
    {
        var at = token.IndexOf('@', StringComparison.Ordinal);
        var name = at > 0 ? token[..at] : token;

        foreach (var attachment in attachments)
        {
            if (
                attachment.Content is not { } content
                || (
                    !string.Equals(attachment.FileName, token, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(attachment.FileName, name, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            var type = EmailAttachmentTypes.Resolve(attachment);

            if (!EmailAttachmentTypes.IsInlineImage(type))
            {
                continue;
            }

            return $"data:{type};base64,{Convert.ToBase64String(content.Span)}";
        }

        return null;
    }

    [GeneratedRegex(
        """(?<prefix>\bsrc\s*=\s*["']?|url\(\s*["']?)cid:(?<token>[^"'\s)>]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _CidReference { get; }
}
