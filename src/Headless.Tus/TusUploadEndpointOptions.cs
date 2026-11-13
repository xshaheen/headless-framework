// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Tus;

/// <summary>
/// Options for one upload endpoint declared through <c>AddUploadEndpoint</c> and served by
/// <c>MapHeadlessTus</c>. Every endpoint has its own instance, validated when the host starts.
/// </summary>
[PublicAPI]
public sealed class TusUploadEndpointOptions
{
    /// <summary>The largest upload the endpoint accepts, in bytes. Required: it must be positive.</summary>
    /// <remarks>
    /// A larger upload is refused with 413, whether its length is declared when it is created or only shows as its
    /// bytes arrive. The server's request body limit is raised to this size for the endpoint's requests, because a
    /// client may send the whole upload in one request. A reverse proxy in front of the host keeps its own limit.
    /// The limit is read on every request and is not stored with an upload, so lowering it also applies to the
    /// uploads already in progress.
    /// </remarks>
    public long MaxSize { get; set; }

    /// <summary>How long an incomplete upload stays resumable after its last write.</summary>
    /// <remarks>
    /// Defaults to 24 hours. The window restarts on every write, and an upload that passes it is answered as not
    /// found and removed by the expired-uploads cleanup. A completed upload never expires. A store that does not
    /// support expiration ignores the value.
    /// </remarks>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(24);
}

internal sealed class TusUploadEndpointOptionsValidator : AbstractValidator<TusUploadEndpointOptions>
{
    public TusUploadEndpointOptionsValidator()
    {
        RuleFor(x => x.MaxSize).GreaterThan(0);
        RuleFor(x => x.Expiration).GreaterThan(TimeSpan.Zero);
    }
}
