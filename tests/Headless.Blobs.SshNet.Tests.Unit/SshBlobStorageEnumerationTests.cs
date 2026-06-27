// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.SshNet;

namespace Tests;

public sealed class SshBlobStorageEnumerationTests
{
    [Theory]
    [InlineData(null, "bucket", "")]
    [InlineData("", "bucket", "")]
    [InlineData("reports", "bucket", "")]
    [InlineData("reports/", "bucket/reports", "reports/")]
    [InlineData("reports/2026", "bucket/reports", "reports/")]
    [InlineData("reports/2026/", "bucket/reports/2026", "reports/2026/")]
    [InlineData("reports/2026/summary.txt", "bucket/reports/2026", "reports/2026/")]
    public void should_start_enumeration_at_deepest_directory_implied_by_prefix(
        string? prefix,
        string expectedDirectory,
        string expectedRelativePrefix
    )
    {
        // Act
        var (directory, relativePrefix) = SshBlobStorage.GetEnumerationScope("bucket", prefix);

        // Assert
        directory.Should().Be(expectedDirectory);
        relativePrefix.Should().Be(expectedRelativePrefix);
    }
}
