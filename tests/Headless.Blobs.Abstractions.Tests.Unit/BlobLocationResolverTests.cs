// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.Internals;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobLocationResolverTests : TestBase
{
    private readonly IBlobNamingNormalizer _normalizer = new TwoTierNormalizer();

    #region Resolve Tests

    [Fact]
    public void should_normalize_container_strict_and_key_segments_lenient()
    {
        // Arrange
        var location = new BlobLocation("My-Bucket", "Folder/Sub/File.TXT");

        // Act
        var (container, key) = BlobLocationResolver.Resolve(location, _normalizer);

        // Assert - container via strict normalizer (upper), each key segment via lenient (lower), rejoined with '/'
        container.Should().Be("MY-BUCKET");
        key.Should().Be("folder/sub/file.txt");
    }

    [Fact]
    public void should_normalize_single_segment_key()
    {
        // Arrange
        var location = new BlobLocation("bucket", "File.TXT");

        // Act
        var (container, key) = BlobLocationResolver.Resolve(location, _normalizer);

        // Assert
        container.Should().Be("BUCKET");
        key.Should().Be("file.txt");
    }

    #endregion

    #region ResolveQuery Tests

    [Fact]
    public void should_normalize_query_prefix_segments_lenient()
    {
        // Arrange
        var query = new BlobQuery("My-Bucket", "Logs/Sub");

        // Act
        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, _normalizer);

        // Assert
        container.Should().Be("MY-BUCKET");
        prefix.Should().Be("logs/sub");
    }

    [Fact]
    public void should_return_null_prefix_when_query_has_no_prefix()
    {
        // Arrange
        var query = new BlobQuery("bucket");

        // Act
        var (container, prefix) = BlobLocationResolver.ResolveQuery(query, _normalizer);

        // Assert
        container.Should().Be("BUCKET");
        prefix.Should().BeNull();
    }

    #endregion

    #region Post-normalization re-validation (lossy normalizer must not reintroduce traversal/sidecar/empty)

    // The default CrossOsNamingNormalizer (file system, SFTP, Redis) strips invalid filename chars (\ / : * ? " < > |).
    // These inputs pass BlobLocation/BlobQuery construction-time validation but normalize INTO a dangerous form, so the
    // resolve seam must re-validate the normalized result.
    private readonly IBlobNamingNormalizer _crossOs = new CrossOsNamingNormalizer();

    [Fact]
    public void should_reject_key_that_normalizes_into_traversal()
    {
        // ".*." has no literal ".." (passes construction); CrossOs strips '*' so each segment collapses to ".."
        var location = new BlobLocation("bucket", ".*./.*./evil.txt");

        var act = () => BlobLocationResolver.Resolve(location, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_key_that_normalizes_into_reserved_sidecar_suffix()
    {
        // "report.hlmet:a" does not end in ".hlmeta" (passes construction); stripping ':' yields "report.hlmeta"
        var location = new BlobLocation("bucket", "report.hlmet:a");

        var act = () => BlobLocationResolver.Resolve(location, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_key_that_normalizes_to_empty()
    {
        var location = new BlobLocation("bucket", ":");

        var act = () => BlobLocationResolver.Resolve(location, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_key_segment_that_normalizes_to_current_directory()
    {
        var location = new BlobLocation("bucket", ".:/file.txt");

        var act = () => BlobLocationResolver.Resolve(location, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_query_prefix_that_normalizes_to_empty()
    {
        // ":" passes BlobQuery construction but normalizes to empty; it must not become a whole-container match
        var query = new BlobQuery("bucket", ":");

        var act = () => BlobLocationResolver.ResolveQuery(query, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_query_prefix_that_normalizes_into_traversal()
    {
        var query = new BlobQuery("bucket", ".*./secret");

        var act = () => BlobLocationResolver.ResolveQuery(query, _crossOs);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_preserve_query_prefix_with_trailing_slash()
    {
        var query = new BlobQuery("bucket", "folder/");

        var (_, prefix) = BlobLocationResolver.ResolveQuery(query, _crossOs);

        prefix.Should().Be("folder/");
    }

    #endregion
}

/// <summary>Normalizer that distinguishes the two tiers: container strict (upper), blob segments lenient (lower).</summary>
file sealed class TwoTierNormalizer : IBlobNamingNormalizer
{
    public string NormalizeContainerName(string containerName) => containerName.ToUpperInvariant();

    public string NormalizeBlobName(string blobName) => blobName.ToLowerInvariant();
}
