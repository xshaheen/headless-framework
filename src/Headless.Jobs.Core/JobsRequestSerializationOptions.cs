// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Immutable per-host settings controlling how job request payloads are converted to and from the byte
/// array representation stored in the persistence layer. Composed by <c>AddHeadlessJobs</c> from the
/// <c>JobsOptionsBuilder</c> configuration (<c>ConfigureRequestJsonOptions</c> / <c>UseGZipCompression</c>)
/// and registered as a singleton, so every <c>IHost</c> owns its own settings — there is no process-global
/// serializer state shared across hosts.
/// </summary>
[PublicAPI]
public sealed class JobsRequestSerializationOptions
{
    /// <summary>Default maximum expanded size of a compressed job request: 64 MiB.</summary>
    public const int DefaultMaxDecompressedRequestBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Shared instance carrying the default settings: default <see cref="JsonSerializerOptions"/> and no
    /// GZip compression. Used when a component operates outside a configured host.
    /// </summary>
    public static JobsRequestSerializationOptions Default { get; } = new();

    /// <summary>
    /// The <see cref="JsonSerializerOptions"/> used to serialize and deserialize job request payloads.
    /// Defaults to <see cref="JsonSerializerOptions.Default"/>.
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; init; } = JsonSerializerOptions.Default;

    /// <summary>
    /// Whether job request payloads are GZip-compressed. When <see langword="false"/> (default), requests
    /// are stored as plain UTF-8 JSON bytes without compression.
    /// </summary>
    public bool UseGZipCompression { get; init; }

    /// <summary>
    /// Maximum expanded size of a compressed job request accepted during reads (decompression-bomb guard).
    /// Defaults to <see cref="DefaultMaxDecompressedRequestBytes"/> (64 MiB).
    /// </summary>
    public int MaxDecompressedRequestBytes { get; init; } = DefaultMaxDecompressedRequestBytes;
}
