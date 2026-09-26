// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs;

/// <summary>Configuration for the signed-URL endpoint that serves blob stores without native presign.</summary>
[PublicAPI]
public sealed class BlobSignedUrlOptions
{
    /// <summary>
    /// The public absolute URL the endpoint is reachable at, without the route prefix — for example
    /// <c>https://api.example.com</c>, or <c>https://example.com/app</c> behind a path base. Required.
    /// </summary>
    /// <remarks>
    /// URLs are often minted outside a request (a background job generating a report), so the public address is
    /// configured rather than read from the current request, which a reverse proxy may also have rewritten.
    /// </remarks>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// The route prefix <c>MapBlobSignedUrlEndpoint</c> maps under and minted URLs point at. Defaults to
    /// <c>/blobs</c>.
    /// </summary>
    public string RoutePrefix { get; set; } = "/blobs";
}

internal sealed class BlobSignedUrlOptionsValidator : AbstractValidator<BlobSignedUrlOptions>
{
    public BlobSignedUrlOptionsValidator()
    {
        RuleFor(x => x.BaseUrl)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(url =>
                url!.IsAbsoluteUri
                && (
                    string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                    || string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                )
                && string.IsNullOrEmpty(url.Query)
                && string.IsNullOrEmpty(url.Fragment)
            )
            .WithMessage("'{PropertyName}' must be an absolute http or https URL with no query or fragment.");

        RuleFor(x => x.RoutePrefix)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(prefix => prefix.Length > 1 && prefix[0] == '/' && prefix.IndexOfAny(['{', '}', '?', '#', '*']) < 0)
            .WithMessage(
                "'{PropertyName}' must be a literal path such as '/blobs': start with '/', not be the root, and contain no route parameters, '?', or '#'."
            );
    }
}
