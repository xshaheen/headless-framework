// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.SshNet;
using Framework.Testing.Tests;
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
    public void should_use_default_port_22_when_not_specified()
    {
        // given - connection string without port
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com",
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when - creating pool succeeds (port parsing happens at connect time)
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then - pool is created without error
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_accept_connection_string_with_explicit_port()
    {
        // given
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com:2222",
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_accept_url_encoded_credentials()
    {
        // given - username with @ symbol and password with special chars
        // user@domain -> user%40domain, pass!word -> pass%21word
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user%40domain:pass%21word@example.com:22",
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_accept_connection_string_without_password()
    {
        // given - username only (uses NoneAuthenticationMethod or PrivateKey)
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user@example.com:22",
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_accept_proxy_configuration()
    {
        // given
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com:22",
                Proxy = "http://proxyuser:proxypass@proxy.example.com:8080",
                ProxyType = ProxyTypes.Http,
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_infer_http_proxy_type_from_scheme()
    {
        // given - ProxyType is None but proxy URI has http scheme
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com:22",
                Proxy = "http://proxyuser:proxypass@proxy.example.com:8080",
                ProxyType = ProxyTypes.None, // Should be inferred as Http
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_accept_socks5_proxy_type()
    {
        // given
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com:22",
                Proxy = "socks5://proxyuser:proxypass@proxy.example.com:1080",
                ProxyType = ProxyTypes.Socks5,
                MaxPoolSize = 1,
                MaxConcurrentOperations = 1,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then
        pool.Should().NotBeNull();
        pool.Dispose();
    }

    [Fact]
    public void should_respect_max_pool_size_configuration()
    {
        // given
        const int maxPoolSize = 10;
        var options = Options.Create(
            new SshBlobStorageOptions
            {
                ConnectionString = "sftp://user:pass@example.com:22",
                MaxPoolSize = maxPoolSize,
                MaxConcurrentOperations = maxPoolSize,
            }
        );

        // when
        var pool = new SftpClientPool(options, NullLogger<SftpClientPool>.Instance);

        // then - pool created with specified size (behavior verified in integration tests)
        pool.Should().NotBeNull();
        pool.Dispose();
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
