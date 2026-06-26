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
}

/// <summary>Normalizer that distinguishes the two tiers: container strict (upper), blob segments lenient (lower).</summary>
file sealed class TwoTierNormalizer : IBlobNamingNormalizer
{
    public string NormalizeContainerName(string containerName) => containerName.ToUpperInvariant();

    public string NormalizeBlobName(string blobName) => blobName.ToLowerInvariant();
}
