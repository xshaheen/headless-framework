// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Headless.Blobs.SshNet;

/// <summary>
/// Channel-based pool of <see cref="SftpClient"/> connections for concurrent SFTP operations.
/// </summary>
/// <remarks>
/// Uses <see cref="System.Threading.Channels.Channel{T}"/> for lock-free, async-first connection reuse
/// (Npgsql-style pooling). The pool creates new connections on demand up to
/// <see cref="SshBlobStorageOptions.MaxPoolSize"/> (and, for open read streams, up to
/// <see cref="SshBlobStorageOptions.MaxReadStreamConnections"/> in <see cref="ReadStreams"/>) and validates each
/// client before returning it to a caller.
/// Invalid or disconnected clients are disposed and replaced. Dispose the pool when the application shuts down
/// to close all open SSH connections.
/// </remarks>
internal sealed class SftpClientPool : IDisposable
{
    private readonly Channel<SftpClient> _idle;
    private readonly SemaphoreSlim _maxConnections;
    private readonly SshBlobStorageOptions _options;
    private readonly ILogger<SftpClientPool> _logger;
    private readonly SftpClientPool? _readStreams;
    private readonly PrivateKeyFile? _privateKey;
    private volatile bool _disposed;

    public SftpClientPool(IOptions<SshBlobStorageOptions> options, ILogger<SftpClientPool> logger)
        : this(options.Value, options.Value.MaxPoolSize, logger, _ReadPrivateKey(options.Value))
    {
        _readStreams = new SftpClientPool(_options, _options.MaxReadStreamConnections, logger, _privateKey);
    }

    private SftpClientPool(
        SshBlobStorageOptions options,
        int maxConnections,
        ILogger<SftpClientPool> logger,
        PrivateKeyFile? privateKey
    )
    {
        _options = options;
        _logger = logger;
        _privateKey = privateKey;
        _idle = Channel.CreateBounded<SftpClient>(
            new BoundedChannelOptions(maxConnections)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
        _maxConnections = new SemaphoreSlim(maxConnections, maxConnections);
    }

    /// <summary>
    /// The separate set of connections, bounded by <see cref="SshBlobStorageOptions.MaxReadStreamConnections"/>, that
    /// backs the streams <see cref="IBlobStorage.OpenReadStreamAsync"/> returns.
    /// </summary>
    /// <remarks>
    /// A returned stream keeps its connection until the caller disposes it, so its hold time is the reader's pace, not
    /// SFTP latency: a signed-URL download to a slow client holds it for the whole transfer. Serving those streams from
    /// this pool means slow readers wait on each other and never starve the store's own uploads, deletes, or info
    /// reads. Read-stream connections are disposed with the owning pool. On the read-stream pool itself this returns
    /// the same instance.
    /// </remarks>
    public SftpClientPool ReadStreams => _readStreams ?? this;

    /// <summary>
    /// Acquires an SFTP client from the pool, reusing an idle connection when available or creating a new one.
    /// </summary>
    /// <param name="ct">Cancellation token. Passed to the connection semaphore and to the SSH handshake.</param>
    /// <returns>A connected <see cref="SftpClient"/>. The caller must return it via <see cref="ReleaseAsync"/> when done.</returns>
    /// <remarks>
    /// The pool is bounded (<see cref="SshBlobStorageOptions.MaxPoolSize"/> connections, or
    /// <see cref="SshBlobStorageOptions.MaxReadStreamConnections"/> for <see cref="ReadStreams"/>) and applies
    /// backpressure by design: there is no acquire timeout, so when every slot is in use this call blocks until a slot
    /// frees or <paramref name="ct"/> is cancelled. A slot is only ever returned by <see cref="ReleaseAsync"/> /
    /// <see cref="Release"/> — the store releases it when an operation finishes, and for
    /// <see cref="IBlobStorage.OpenReadStreamAsync"/> the returned stream owns its <see cref="ReadStreams"/> client until
    /// the caller disposes it. A caller that never disposes an <c>OpenReadStreamAsync</c> result therefore permanently
    /// leaks a read-stream slot; after <see cref="SshBlobStorageOptions.MaxReadStreamConnections"/> such leaks every
    /// later <c>OpenReadStreamAsync</c> blocks until its own cancellation fires, while other operations keep their own
    /// pool. Disposing acquired clients / download results is a caller responsibility; size both limits for peak
    /// concurrency and always pass a cancellation token.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed.</exception>
    /// <exception cref="Renci.SshNet.Common.SshConnectionException">Thrown when a new SSH connection cannot be established.</exception>
    public async ValueTask<SftpClient> AcquireAsync(CancellationToken ct)
    {
        Ensure.NotDisposed(_disposed, this);

        // The semaphore counts checked-out clients; an idle client holds no slot. Every acquire therefore waits on the
        // semaphore first, and every release frees a slot after returning its client to the idle channel, which wakes
        // exactly one waiter with an idle client ready for it. Letting idle clients keep their slots instead would leave
        // a waiter parked on the semaphore while the client it needs sits idle, forever once nobody else acquires.
        // Total connections stay within the limit: a new one is only created when no idle client exists.
        await _maxConnections.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (_idle.Reader.TryRead(out var client))
            {
                if (_Validate(client))
                {
                    _logger.LogAcquiredPooledClient();
                    return client;
                }

                // Invalid pooled client: dispose it and create a replacement under the slot this call holds.
                _DisposeClient(client);
            }

            return await _CreateAndConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _ReleaseSlot();
            throw;
        }
    }

    /// <summary>
    /// Returns a client to the pool if it is still connected and the pool has capacity; otherwise disposes it.
    /// </summary>
    /// <param name="client">The client to return. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public ValueTask ReleaseAsync(SftpClient client)
    {
        Release(client);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Synchronously returns a client to the pool if it is still connected and the pool has capacity; otherwise disposes it.
    /// </summary>
    /// <param name="client">The client to return. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public void Release(SftpClient client)
    {
        Argument.IsNotNull(client);

        if (_disposed || !client.IsConnected)
        {
            _DisposeClient(client);
        }
        else if (!_idle.Writer.TryWrite(client))
        {
            // Pool full (or completed by a concurrent dispose), dispose excess
            _logger.LogPoolFullDisposingExcess();
            _DisposeClient(client);
        }
        else
        {
            _logger.LogReturnedClientToPool();
        }

        // Free the slot only after the client is back in the idle channel, so the waiter it wakes finds it there.
        _ReleaseSlot();
    }

    private void _ReleaseSlot()
    {
        try
        {
            _maxConnections.Release();
        }
        catch (ObjectDisposedException)
        {
            // The pool was disposed while this client was checked out, so there is no slot left to return. A stream
            // disposed during host shutdown lands here and must not throw out of its own Dispose.
        }
    }

    private async ValueTask<SftpClient> _CreateAndConnectAsync(CancellationToken ct)
    {
        var connectionInfo = _BuildConnectionInfo(_options, _privateKey);
        var client = new SftpClient(connectionInfo);

        _logger.LogCreatingConnection(connectionInfo.Host, connectionInfo.Port);

        await client.ConnectAsync(ct).ConfigureAwait(false);

        _logger.LogConnected(connectionInfo.Host, connectionInfo.Port, client.WorkingDirectory);

        return client;
    }

    // Reading the key consumes (and disposes) the option's stream, so it is read once, when the owning pool is created,
    // and every connection of both pools offers the same parsed key. Parsing it per connection would authenticate only
    // the first one.
    private static PrivateKeyFile? _ReadPrivateKey(SshBlobStorageOptions options)
    {
        return options.PrivateKey is null ? null : new PrivateKeyFile(options.PrivateKey, options.PrivateKeyPassPhrase);
    }

    private static ConnectionInfo _BuildConnectionInfo(SshBlobStorageOptions options, PrivateKeyFile? privateKey)
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

        if (privateKey is not null)
        {
            authenticationMethods.Add(new PrivateKeyAuthenticationMethod(username, privateKey));
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

        // Only the owning pool disposes the key; the read-stream pool shares it and has no read-stream pool of its own.
        if (_readStreams is not null)
        {
            _readStreams.Dispose();
            _privateKey?.Dispose();
        }

        _logger.LogPoolDisposed();
    }
}
