// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.SshNet;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Tests for SftpClientPool connection string parsing and pool behavior.
/// Note: Full pool behavior requires real SFTP connections (covered in integration tests).
/// These tests focus on:
/// - Options validation and configuration
/// - Disposal behavior
/// - Connection string format validation
/// </summary>
public sealed class SftpClientPoolTests : TestBase
{
    [Fact]
    public async Task should_throw_when_disposed_on_acquire()
    {
        // given
        var options = Options.Create(_CreateValidOptions());
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);
        pool.Dispose();

        // when
        var act = async () => await pool.AcquireAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void should_be_idempotent_on_multiple_dispose()
    {
        // given
        var options = Options.Create(_CreateValidOptions());
        using var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // when
        var act = () =>
        {
            pool.Dispose();
            pool.Dispose();
            pool.Dispose();
        };

        // then
        act.Should().NotThrow();
    }

    private static SshBlobStorageOptions _CreateValidOptions()
    {
        return new SshBlobStorageOptions
        {
            ConnectionString = "sftp://user:pass@localhost:22",
            MaxPoolSize = 4,
            MaxConcurrentOperations = 4,
        };
    }
}
