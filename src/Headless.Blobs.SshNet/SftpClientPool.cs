// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Headless.Blobs.SshNet;

/// <summary>
/// Channel-based SFTP client pool for concurrent operations.
/// Uses System.Threading.Channels for async-first pooling (Npgsql pattern).
/// </summary>
public sealed class SftpClientPool : IDisposable
{
    private readonly Channel<SftpClient> _idle;
    private readonly SemaphoreSlim _maxConnections;
    private readonly SshBlobStorageOptions _options;
    private readonly ILogger<SftpClientPool> _logger;
    private volatile bool _disposed;

    public SftpClientPool(IOptions<SshBlobStorageOptions> options, ILogger<SftpClientPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _idle = Channel.CreateBounded<SftpClient>(
            new BoundedChannelOptions(_options.MaxPoolSize)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
        _maxConnections = new SemaphoreSlim(_options.MaxPoolSize, _options.MaxPoolSize);
    }

    /// <summary>
    /// Acquires an SFTP client from the pool. Creates new if pool empty.
    /// </summary>
    public async ValueTask<SftpClient> AcquireAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Try to get existing connection (non-blocking)
        // Pooled clients hold a semaphore slot, so if validation fails we must release it
        while (_idle.Reader.TryRead(out var client))
        {
            if (_Validate(client))
            {
                _logger.LogAcquiredPooledClient();
                return client;
            }

            // Client invalid - dispose and release its semaphore slot
            _DisposeClient(client);
            _maxConnections.Release();
        }

        // No valid pooled connection - wait for available slot and create new
        await _maxConnections.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check pool after acquiring semaphore (someone may have returned a client)
            while (_idle.Reader.TryRead(out var client))
            {
                if (_Validate(client))
                {
                    // Release the slot we acquired since pooled client has its own
                    _maxConnections.Release();
                    _logger.LogAcquiredPooledClientAfterWait();
                    return client;
                }

                // Invalid pooled client - dispose and release its slot
                // We keep the slot we acquired for creating a new connection
                _DisposeClient(client);
                _maxConnections.Release();
            }

            return await _CreateAndConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _maxConnections.Release();
            throw;
        }
    }

    /// <summary>
    /// Returns client to pool if healthy, otherwise disposes.
    /// </summary>
    public ValueTask ReleaseAsync(SftpClient client)
    {
        Argument.IsNotNull(client);

        if (_disposed || !client.IsConnected)
        {
            _DisposeClient(client);
            _maxConnections.Release();
            return ValueTask.CompletedTask;
        }

        if (!_idle.Writer.TryWrite(client))
        {
            // Pool full, dispose excess
            _logger.LogPoolFullDisposingExcess();
            _DisposeClient(client);
            _maxConnections.Release();
        }
        else
        {
            _logger.LogReturnedClientToPool();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask<SftpClient> _CreateAndConnectAsync(CancellationToken ct)
    {
        var connectionInfo = _BuildConnectionInfo(_options);
        var client = new SftpClient(connectionInfo);

        _logger.LogCreatingConnection(connectionInfo.Host, connectionInfo.Port);

        await client.ConnectAsync(ct).ConfigureAwait(false);

        _logger.LogConnected(connectionInfo.Host, connectionInfo.Port, client.WorkingDirectory);

        return client;
    }

    private static ConnectionInfo _BuildConnectionInfo(SshBlobStorageOptions options)
    {
        Argument.IsNotNull(options);

        if (
            !Uri.TryCreate(options.ConnectionString, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse connection string uri");
        }

        var userParts = uri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var username = Uri.UnescapeDataString(userParts[0]);
        var password = Uri.UnescapeDataString(userParts.Length > 1 ? userParts[1] : string.Empty);
        var port = uri.Port > 0 ? uri.Port : 22;

        var authenticationMethods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(password))
        {
            authenticationMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (options.PrivateKey is not null)
        {
            authenticationMethods.Add(
                new PrivateKeyAuthenticationMethod(
                    username,
                    new PrivateKeyFile(options.PrivateKey, options.PrivateKeyPassPhrase)
                )
            );
        }

        if (authenticationMethods.Count == 0)
        {
            authenticationMethods.Add(new NoneAuthenticationMethod(username));
        }

        if (string.IsNullOrEmpty(options.Proxy))
        {
            return new ConnectionInfo(uri.Host, port, username, [.. authenticationMethods]);
        }

        if (
            !Uri.TryCreate(options.Proxy, UriKind.Absolute, out var proxyUri) || string.IsNullOrEmpty(proxyUri.UserInfo)
        )
        {
            throw new InvalidOperationException("Unable to parse proxy uri");
        }

        var proxyParts = proxyUri.UserInfo.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var proxyUsername = Uri.UnescapeDataString(proxyParts[0]);
        var proxyPassword = proxyParts.Length > 1 ? Uri.UnescapeDataString(proxyParts[1]) : null;

        var proxyType = options.ProxyType;

        if (proxyType is ProxyTypes.None && proxyUri.Scheme.StartsWith("http", StringComparison.Ordinal))
        {
            proxyType = ProxyTypes.Http;
        }

        return new ConnectionInfo(
            uri.Host,
            port,
            username,
            proxyType,
            proxyUri.Host,
            proxyUri.Port,
            proxyUsername,
            proxyPassword,
            [.. authenticationMethods]
        );
    }

    private bool _Validate(SftpClient client)
    {
        if (!client.IsConnected)
        {
            _logger.LogClientNotConnected();
            return false;
        }

        try
        {
            // Lightweight validation - check session is responsive
            _ = client.WorkingDirectory;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogClientValidationFailed(ex);
            return false;
        }
    }

    private void _DisposeClient(SftpClient client)
    {
        try
        {
            if (client.IsConnected)
            {
                _logger.LogDisconnectingClient(client.ConnectionInfo.Host, client.ConnectionInfo.Port);
                client.Disconnect();
            }
        }
        catch (Exception e)
        {
            _logger.LogErrorDisconnectingClient(e);
        }
        finally
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idle.Writer.Complete();

        _logger.LogDisposingPool();

        while (_idle.Reader.TryRead(out var client))
        {
            _DisposeClient(client);
        }

        _maxConnections.Dispose();

        _logger.LogPoolDisposed();
    }
}
