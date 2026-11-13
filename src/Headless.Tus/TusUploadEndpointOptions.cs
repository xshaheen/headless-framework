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

    /// <summary>
    /// The content types an upload may declare, such as <c>image/png</c>, compared without regard to letter case.
    /// Empty by default, which allows every content type.
    /// </summary>
    /// <remarks>
    /// The client declares the content type in the <c>filetype</c> metadata field, or in <c>type</c> when it sends
    /// no <c>filetype</c>; any parameter after a <c>;</c> is ignored. While the list has an entry, a creating
    /// request that declares no content type, or one outside the list, is refused with 415. The list also feeds
    /// the check of the upload's first bytes that <see cref="VerifyFileSignature"/> describes.
    /// </remarks>
    public ISet<string> AllowedContentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file name extensions an upload may declare, such as <c>png</c>, with or without the leading dot and
    /// compared without regard to letter case. Empty by default, which allows every extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client declares the file name in the <c>filename</c> metadata field, or in <c>name</c> when it sends no
    /// <c>filename</c>. While the list has an entry, a creating request that declares no file name, a name without
    /// an extension, or an extension outside the list is refused with 415. The list also feeds the check of the
    /// upload's first bytes that <see cref="VerifyFileSignature"/> describes.
    /// </para>
    /// <para>
    /// These are file name extensions. They are unrelated to the tus protocol extensions that tusdotnet's
    /// <c>AllowedExtensions</c> selects.
    /// </para>
    /// </remarks>
    public ISet<string> AllowedFileExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The metadata fields every upload must carry with a value, by their case-sensitive key. Empty by default.
    /// </summary>
    /// <remarks>
    /// A creating request that leaves one out, or sends it without a value, is refused with 400 and a message that
    /// names the field.
    /// </remarks>
    public ISet<string> RequiredMetadata { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Whether an upload of zero bytes is accepted. Defaults to <see langword="false"/>.</summary>
    /// <remarks>
    /// A creating request that declares a length of zero is otherwise refused with 400. An upload whose length is
    /// deferred is not empty when it is created, so the option does not apply to it.
    /// </remarks>
    public bool AllowEmptyUploads { get; set; }

    /// <summary>
    /// Whether the first bytes of an upload are checked against <see cref="AllowedContentTypes"/> and
    /// <see cref="AllowedFileExtensions"/>. Defaults to <see langword="true"/>; it has no effect while both lists
    /// are empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check runs on the upload's first write and recognizes the file format from its leading bytes. The upload
    /// is refused with 415 when the recognized format is not one the lists allow, and when no format is recognized
    /// although the declared content type or extension belongs to a format that can be recognized. An allowed
    /// extension also allows the format of its usual content type, so <c>jpeg</c> allows a file recognized as
    /// <c>jpg</c>. A declared type with no signature, such as <c>text/csv</c>, is taken as declared.
    /// </para>
    /// <para>
    /// Formats built on a container cannot be told apart from their first bytes, so they are checked down to the
    /// container only: an allowed Office Open XML format (docx, xlsx, pptx) accepts any ZIP archive, and an allowed
    /// legacy Office format (doc, xls, ppt) accepts any compound file.
    /// </para>
    /// <para>
    /// The first write must carry the bytes the check reads, a few hundred, or the whole upload when it is
    /// shorter; a shorter first write is refused with 400, as is one on an upload whose length is still deferred.
    /// A refused upload is deleted. When the refused bytes arrive with the creating request, that request still
    /// answers 201 with an offset of zero, and the upload is gone on the client's next request.
    /// </para>
    /// </remarks>
    public bool VerifyFileSignature { get; set; } = true;
}

internal sealed class TusUploadEndpointOptionsValidator : AbstractValidator<TusUploadEndpointOptions>
{
    public TusUploadEndpointOptionsValidator()
    {
        RuleFor(x => x.MaxSize).GreaterThan(0);
        RuleFor(x => x.Expiration).GreaterThan(TimeSpan.Zero);
        RuleForEach(x => x.AllowedContentTypes).NotEmpty();
        RuleForEach(x => x.AllowedFileExtensions).NotEmpty();
        RuleForEach(x => x.RequiredMetadata).NotEmpty();
    }
}
