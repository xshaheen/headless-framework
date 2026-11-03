// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using System.Security.Cryptography;
using Headless.Blobs.SshNet;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Renci.SshNet;

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

    [Fact]
    public async Task should_dispose_a_client_released_after_the_pool_is_disposed()
    {
        // given: a client still checked out when the host shuts the pool down
        var options = Options.Create(_CreateValidOptions());
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);
        using var client = new SftpClient("localhost", "user", "pass");
        pool.Dispose();

        // when
        var act = async () => await pool.ReleaseAsync(client);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void should_serve_read_streams_from_a_separate_pool()
    {
        // given
        var options = Options.Create(_CreateValidOptions());
        using var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // when
        var readStreams = pool.ReadStreams;

        // then: a separate pool, whose own read-stream pool is itself (no further nesting)
        readStreams.Should().NotBeSameAs(pool);
        readStreams.ReadStreams.Should().BeSameAs(readStreams);
    }

    [Fact]
    public async Task should_dispose_the_read_stream_pool_with_the_pool()
    {
        // given
        var options = Options.Create(_CreateValidOptions());
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);
        var readStreams = pool.ReadStreams;

        // when
        pool.Dispose();
        var act = async () => await readStreams.AcquireAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_offer_the_private_key_on_every_new_connection_when_the_key_is_a_stream()
    {
        // given: key auth against a port nothing listens on, so each acquire builds its credentials, then fails to
        // connect; the main pool and the read-stream pool each open connections of their own
        using var rsa = RSA.Create(2048);
        var options = _CreateValidOptions();
        options.ConnectionString = "sftp://user@127.0.0.1:1";
        options.PrivateKey = new MemoryStream(Encoding.ASCII.GetBytes(rsa.ExportRSAPrivateKeyPem()));
        using var pool = new SftpClientPool(Options.Create(options), NullLogger<SftpClientPool>.Instance);

        // when
        var first = async () => await pool.AcquireAsync(AbortToken);
        var second = async () => await pool.AcquireAsync(AbortToken);
        var readStream = async () => await pool.ReadStreams.AcquireAsync(AbortToken);

        // then: every attempt reaches the network instead of failing on a key stream the first one consumed
        await first.Should().ThrowAsync<SocketException>();
        await second.Should().ThrowAsync<SocketException>();
        await readStream.Should().ThrowAsync<SocketException>();
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
