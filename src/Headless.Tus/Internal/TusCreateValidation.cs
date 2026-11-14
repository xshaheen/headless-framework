// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace Headless.Tus.Internal;

/// <summary>
/// Refuses an upload before it starts: the endpoint's own rules on what the client declares, then the
/// application's validators.
/// </summary>
/// <remarks>
/// It runs as tusdotnet's before-create event, after tusdotnet has checked the declared length against the
/// endpoint's maximum size and before the store creates anything. What the client declares is only a claim; the
/// upload's first bytes are checked against it when they arrive.
/// </remarks>
internal static class TusCreateValidation
{
    /// <summary>Lets the creating request go on, or sets the refusal tusdotnet answers it with.</summary>
    /// <param name="context">tusdotnet's before-create context for the request.</param>
    /// <param name="endpoint">The endpoint the request reached.</param>
    /// <param name="options">The endpoint's options.</param>
    /// <param name="caller">The stamp the upload gets.</param>
    public static async Task ValidateAsync(
        BeforeCreateContext context,
        TusEndpointRegistration endpoint,
        TusUploadEndpointOptions options,
        TusUploadStamp caller
    )
    {
        if (_FindRefusal(context, options) is { } refusal)
        {
            context.FailRequest(refusal.Status, refusal.Message);

            return;
        }

        if (endpoint.Validators.Count == 0)
        {
            return;
        }

        var request = new TusUploadRequest
        {
            Length = context.UploadLengthIsDeferred ? null : context.UploadLength,
            Metadata = context.Metadata.ToDictionary(
                static entry => entry.Key,
                static entry => entry.Value.GetString(Encoding.UTF8),
                StringComparer.Ordinal
            ),
            TenantId = caller.TenantId,
            OwnerKey = caller.OwnerKey,
            HttpContext = context.HttpContext,
        };

        foreach (var validatorType in endpoint.Validators)
        {
            var validator = (ITusUploadValidator)context.HttpContext.RequestServices.GetRequiredService(validatorType);
            var result = await validator.ValidateAsync(request, context.CancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                context.FailRequest((HttpStatusCode)result.StatusCode.Value, result.Message);

                return;
            }
        }
    }

    /// <summary>
    /// The content type the client declares for the upload, without parameters, or <see langword="null"/> when it
    /// declares none. Clients disagree on the field: tus-js-client's documentation uses <c>filetype</c>, and Uppy
    /// also sends <c>type</c>.
    /// </summary>
    public static string? DeclaredContentType(IReadOnlyDictionary<string, Metadata> metadata)
    {
        if (_FirstValue(metadata, "filetype", "type") is not { } declared)
        {
            return null;
        }

        var parameters = declared.IndexOf(';', StringComparison.Ordinal);
        var contentType = (parameters < 0 ? declared : declared[..parameters]).Trim();

        return contentType.Length == 0 ? null : contentType;
    }

    /// <summary>
    /// The extension of the file name the client declares for the upload, without its dot, or
    /// <see langword="null"/> when it declares no name or a name without an extension. The name is in
    /// <c>filename</c>, or in <c>name</c>, which Uppy also sends.
    /// </summary>
    public static string? DeclaredExtension(IReadOnlyDictionary<string, Metadata> metadata)
    {
        if (_FirstValue(metadata, "filename", "name") is not { } fileName)
        {
            return null;
        }

        var extension = NormalizeExtension(Path.GetExtension(fileName.AsSpan()));

        return extension.Length == 0 ? null : extension.ToString();
    }

    /// <summary>Returns <paramref name="extension"/> as the allow-list compares it: trimmed, with no leading dot.</summary>
    public static ReadOnlySpan<char> NormalizeExtension(ReadOnlySpan<char> extension)
    {
        return extension.Trim().TrimStart('.');
    }

    private static (HttpStatusCode Status, string Message)? _FindRefusal(
        BeforeCreateContext context,
        TusUploadEndpointOptions options
    )
    {
        // tusdotnet takes a key that starts with a tab or another white-space character other than a space as a
        // key of its own, while the stored metadata is written with each entry trimmed. Such a key would be
        // checked here under one name and read back later under another, so "\tfiletype" could carry a content
        // type no rule ever saw.
        foreach (var key in context.Metadata.Keys)
        {
            if (_HasWhiteSpaceOrControl(key))
            {
                return (
                    HttpStatusCode.BadRequest,
                    "An Upload-Metadata key cannot contain white space or a control character."
                );
            }
        }

        foreach (var field in options.RequiredMetadata)
        {
            if (_FirstValue(context.Metadata, field) is null)
            {
                return (HttpStatusCode.BadRequest, $"The upload must carry a value in the metadata field \"{field}\".");
            }
        }

        if (options.AllowedContentTypes.Count > 0)
        {
            if (DeclaredContentType(context.Metadata) is not { } contentType)
            {
                return (
                    HttpStatusCode.UnsupportedMediaType,
                    "The upload must declare its content type in the metadata field \"filetype\"."
                );
            }

            if (!options.AllowedContentTypes.Contains(contentType))
            {
                return (HttpStatusCode.UnsupportedMediaType, $"The content type \"{contentType}\" is not allowed.");
            }
        }

        if (options.AllowedFileExtensions.Count > 0)
        {
            if (DeclaredExtension(context.Metadata) is not { } extension)
            {
                return (
                    HttpStatusCode.UnsupportedMediaType,
                    "The upload must declare a file name with an extension in the metadata field \"filename\"."
                );
            }

            if (!_IsAllowedExtension(options, extension))
            {
                return (HttpStatusCode.UnsupportedMediaType, $"The file extension \"{extension}\" is not allowed.");
            }
        }

        if (context.UploadLength == 0 && !options.AllowEmptyUploads)
        {
            return (HttpStatusCode.BadRequest, "An empty upload is not allowed.");
        }

        return null;
    }

    private static bool _HasWhiteSpaceOrControl(string key)
    {
        foreach (var character in key)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }

    private static bool _IsAllowedExtension(TusUploadEndpointOptions options, string extension)
    {
        foreach (var allowed in options.AllowedFileExtensions)
        {
            if (NormalizeExtension(allowed).Equals(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The value of the first of <paramref name="keys"/> the client sent with one, decoded as UTF-8. A key sent
    /// without a value, or with a blank one, counts as not sent.
    /// </summary>
    private static string? _FirstValue(IReadOnlyDictionary<string, Metadata> metadata, params ReadOnlySpan<string> keys)
    {
        foreach (var key in keys)
        {
            if (
                metadata.TryGetValue(key, out var entry)
                && entry.GetString(Encoding.UTF8) is var value
                && !string.IsNullOrWhiteSpace(value)
            )
            {
                return value;
            }
        }

        return null;
    }
}
