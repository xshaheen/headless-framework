// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using FileSignatures;
using Headless.IO;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Parsers;

namespace Headless.Tus.Internal;

/// <summary>
/// Checks the first bytes of an upload against the file types its endpoint allows, so that what a client declares
/// when it creates an upload is not the only evidence of what it sends.
/// </summary>
/// <remarks>
/// <para>
/// It runs as tusdotnet's before-write event, under the upload's file lock and before the store reads the request
/// body. It reads the body through the request's pipe without consuming it, which is safe only because the store
/// tusdotnet works against always takes the body from that same pipe.
/// </para>
/// <para>
/// The first bytes decide only the formats that have a signature there. A format built on a container, such as
/// Office Open XML on ZIP or legacy Office on a compound file, keeps what identifies it at the end of the file,
/// so an allowed format of that kind is accepted on its container.
/// </para>
/// </remarks>
internal sealed class TusContentCheck(
    ITusStore store,
    TusUploadRemover remover,
    IFileFormatInspector inspector,
    IMimeTypeProvider mimeTypes
)
{
    /// <summary>The most a first write is asked to carry, whatever a format's signature needs.</summary>
    private const int _MaxHeadLength = 4096;

    /// <summary>
    /// The formats a declared or allowed type is matched to. The registered inspector does not list the formats
    /// it knows, so these are the ones FileSignatures ships.
    /// </summary>
    private static readonly FileFormat[] _Formats = [.. FileFormatLocator.GetFormats()];

    /// <summary>
    /// How many leading bytes the check reads: enough for the longest signature among the formats that have one
    /// at the start of the file.
    /// </summary>
    private static readonly int _HeadLength = Math.Min(
        _MaxHeadLength,
        _Formats.Where(static format => !_IsDecidedBeyondTheHead(format)).Max(static format => format.HeaderLength)
    );

    /// <summary>
    /// Lets the write go on, or sets the refusal tusdotnet answers it with and removes the upload.
    /// </summary>
    /// <param name="context">tusdotnet's before-write context for the request.</param>
    /// <param name="options">The options of the endpoint the request reached.</param>
    /// <param name="tenantId">The tenant the request runs in, or <see langword="null"/> for none.</param>
    /// <param name="createdByThisRequest">
    /// Whether the write is the data a creating request carries, not a request of its own.
    /// </param>
    public async Task CheckAsync(
        BeforeWriteContext context,
        TusUploadEndpointOptions options,
        string? tenantId,
        bool createdByThisRequest
    )
    {
        if (context.UploadOffset != 0 || !_IsEnabled(options))
        {
            return;
        }

        var head = await _PeekAsync(context.HttpContext.Request.BodyReader, context.CancellationToken)
            .ConfigureAwait(false);

        // A write with no bytes leaves the offset at zero, so the next write is still the first one.
        if (head.Length == 0)
        {
            return;
        }

        if (head.Length < _HeadLength && !await _IsWholeUploadAsync(context, head.Length).ConfigureAwait(false))
        {
            await _RefuseAsync(
                    context,
                    HttpStatusCode.BadRequest,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The first write of an upload must carry its first {_HeadLength} bytes, or the whole upload when it is shorter."
                    ),
                    tenantId,
                    createdByThisRequest
                )
                .ConfigureAwait(false);

            return;
        }

        if (await _FindMismatchAsync(context, options, head).ConfigureAwait(false) is { } mismatch)
        {
            await _RefuseAsync(context, HttpStatusCode.UnsupportedMediaType, mismatch, tenantId, createdByThisRequest)
                .ConfigureAwait(false);
        }
    }

    private static bool _IsEnabled(TusUploadEndpointOptions options)
    {
        return options.VerifyFileSignature
            && (options.AllowedContentTypes.Count > 0 || options.AllowedFileExtensions.Count > 0);
    }

    /// <summary>
    /// Returns up to <see cref="_HeadLength"/> leading bytes of the request body, fewer only when the body ends
    /// first, and leaves the body as it was.
    /// </summary>
    private static async ValueTask<byte[]> _PeekAsync(PipeReader body, CancellationToken cancellationToken)
    {
        var read = await body.ReadAtLeastAsync(_HeadLength, cancellationToken).ConfigureAwait(false);
        var buffer = read.Buffer;
        var head = buffer.Slice(0, Math.Min(buffer.Length, _HeadLength)).ToArray();

        // Nothing is consumed or marked as examined, so the store's first read returns these same bytes at once.
        body.AdvanceTo(buffer.Start);

        return head;
    }

    /// <summary>Whether <paramref name="received"/> bytes are the whole upload.</summary>
    private async ValueTask<bool> _IsWholeUploadAsync(BeforeWriteContext context, int received)
    {
        // A write may declare the length its creating request deferred. tusdotnet has validated the header by
        // now, and stores it only after this event.
        var length = long.TryParse(
            context.HttpContext.Request.Headers["Upload-Length"],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var declared
        )
            ? declared
            : await store.GetUploadLengthAsync(context.FileId, context.CancellationToken).ConfigureAwait(false);

        // A length that is still deferred may be anything, so it counts as more than what arrived.
        return length <= received;
    }

    /// <summary>
    /// Returns why the upload's first bytes do not fit the endpoint's allow-lists, or <see langword="null"/> when
    /// they do.
    /// </summary>
    private async ValueTask<string?> _FindMismatchAsync(
        BeforeWriteContext context,
        TusUploadEndpointOptions options,
        byte[] head
    )
    {
        var allowed = _AllowedTypes(options);

        if (_Detect(head) is { } detected)
        {
            return
                allowed.Covers(detected)
                // The inspector reports a container when the first bytes cannot tell which format is built on it.
                || _Formats.Any(format => allowed.Covers(format) && detected.GetType().IsInstanceOfType(format))
                ? null
                : $"The content of the upload is \"{detected.MediaType}\", which is not allowed.";
        }

        // An allowed format the first bytes cannot decide, whose container the inspector does not report either.
        if (
            _Formats.Any(format =>
                _IsDecidedBeyondTheHead(format) && allowed.Covers(format) && _StartsWith(head, format)
            )
        )
        {
            return null;
        }

        // Nothing was recognized. That is expected of a type with no signature, such as plain text, and it
        // contradicts a declared type that has one.
        var declared = await _DeclaredTypesAsync(context, options).ConfigureAwait(false);

        return _Formats.Any(declared.Covers) ? "The content of the upload is not of the type it declares." : null;
    }

    /// <summary>
    /// The format the inspector recognizes in <paramref name="head"/>, or <see langword="null"/> for none.
    /// </summary>
    private FileFormat? _Detect(byte[] head)
    {
        // The inspector needs a seekable stream.
        using var stream = new MemoryStream(head, writable: false);

        try
        {
            return inspector.DetermineFileFormat(stream);
        }
        catch (Exception)
        {
#pragma warning disable ERP022 // The fix makes the code worse: the inspector hands the bytes to a container format's parser, which fails on a file cut short with whatever exception that parser throws, and that failure is the answer "not recognized".
            return null;
#pragma warning restore ERP022
        }
    }

    /// <summary>
    /// Whether what identifies <paramref name="format"/> lies beyond the start of the file. FileSignatures marks
    /// such a format with a header that has no end.
    /// </summary>
    private static bool _IsDecidedBeyondTheHead(FileFormat format)
    {
        return format.HeaderLength == int.MaxValue;
    }

    private static bool _StartsWith(byte[] head, FileFormat format)
    {
        var signature = format.Signature;

        if (head.Length - format.Offset < signature.Count)
        {
            return false;
        }

        for (var index = 0; index < signature.Count; index++)
        {
            if (head[format.Offset + index] != signature[index])
            {
                return false;
            }
        }

        return true;
    }

    private TypeSet _AllowedTypes(TusUploadEndpointOptions options)
    {
        var types = new TypeSet();

        foreach (var contentType in options.AllowedContentTypes)
        {
            types.ContentTypes.Add(contentType);
        }

        foreach (var extension in options.AllowedFileExtensions)
        {
            _AddExtension(types, TusCreateValidation.NormalizeExtension(extension).ToString());
        }

        return types;
    }

    /// <summary>
    /// The content type and extension the upload declared when it was created, each only when the endpoint has an
    /// allow-list for it: a value no list was applied to is whatever the client typed.
    /// </summary>
    private async ValueTask<TypeSet> _DeclaredTypesAsync(BeforeWriteContext context, TusUploadEndpointOptions options)
    {
        var types = new TypeSet();

        if (store is not ITusCreationStore creationStore)
        {
            return types;
        }

        var raw = await creationStore
            .GetUploadMetadataAsync(context.FileId, context.CancellationToken)
            .ConfigureAwait(false);

        var metadata = MetadataParser.ParseAndValidate(MetadataParsingStrategy.AllowEmptyValues, raw).Metadata;

        if (
            options.AllowedContentTypes.Count > 0
            && TusCreateValidation.DeclaredContentType(metadata) is { } contentType
        )
        {
            types.ContentTypes.Add(contentType);
        }

        if (options.AllowedFileExtensions.Count > 0 && TusCreateValidation.DeclaredExtension(metadata) is { } extension)
        {
            _AddExtension(types, extension);
        }

        return types;
    }

    /// <summary>
    /// Adds <paramref name="extension"/> and the content type files with it usually have, so that an extension
    /// spelled differently from the one a format is known by, such as <c>jpeg</c> for <c>jpg</c>, still names
    /// that format.
    /// </summary>
    private void _AddExtension(TypeSet types, string extension)
    {
        types.Extensions.Add(extension);

        if (mimeTypes.TryGetMimeType($"file.{extension}", out var contentType))
        {
            types.ContentTypes.Add(contentType);
        }
    }

    /// <summary>
    /// Sets the refusal on <paramref name="context"/> and removes the upload it refuses. The refusal stands
    /// whether or not the removal succeeds; an upload that could not be removed stays until it expires.
    /// </summary>
    private async ValueTask _RefuseAsync(
        BeforeWriteContext context,
        HttpStatusCode status,
        string message,
        string? tenantId,
        bool createdByThisRequest
    )
    {
        context.FailRequest(status, message);

        if (createdByThisRequest)
        {
            // tusdotnet still answers a creating request with 201 and reads the new upload's offset to do so,
            // so the upload must outlive the response.
            remover.RemoveAfterResponse(context.HttpContext, context.FileId, tenantId);

            return;
        }

        await remover.RemoveAsync(context.FileId).ConfigureAwait(false);
    }

    /// <summary>Content types and file extensions, as an allow-list or an upload's declaration names them.</summary>
    private sealed class TypeSet
    {
        public HashSet<string> ContentTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Covers(FileFormat format)
        {
            return ContentTypes.Contains(format.MediaType) || Extensions.Contains(format.Extension);
        }
    }
}
