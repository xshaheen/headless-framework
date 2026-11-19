// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using DotNet.Testcontainers.Configurations;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.RabbitMq;

namespace Tests;

[Collection("RabbitMqConnectivity")]
public sealed class RabbitMqTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortRabbitMqFixture _broker = new();

    [Fact]
    public override Task should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back()
    {
        return base.should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back();
    }

    protected override ValueTask StartBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.StartAsync(cancellationToken);
    }

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseRabbitMq(options =>
        {
            options.HostName = _broker.ForwardedHost;
            options.Port = _broker.ForwardedPort;
            options.UserName = RabbitMqBuilder.DefaultUsername;
            options.Password = RabbitMqBuilder.DefaultPassword;
        });
    }

    protected override Task InterruptBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.Container.StopAsync(cancellationToken);
    }

    protected override Task RestoreBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.RestartAsync(cancellationToken);
    }

    protected override ValueTask StopBrokerAsync()
    {
        return ((IAsyncDisposable)_broker).DisposeAsync();
    }
}

[CollectionDefinition("RabbitMqConnectivity", DisableParallelization = true)]
public sealed class RabbitMqConnectivityCollection;

/// <summary>
/// A RabbitMQ of its own behind a <see cref="BrokerPortForwarder"/>, so a stop and start brings it back at the address
/// the host connects to.
/// </summary>
/// <remarks>
/// Each start is bounded by <see cref="_StartupTimeout"/>. The stock readiness wait, for the broker to log
/// <c>Server startup complete</c>, has no timeout of its own, so a broker that never logs it held the test until the
/// module's timeout dumped the whole module. A bounded start fails this one test with the broker's log instead, which
/// tells a slow Erlang boot from a broker that never started.
/// </remarks>
internal sealed class FixedPortRabbitMqFixture : HeadlessRabbitMqFixture
{
    // A fresh broker boots in seconds and in under a minute on a loaded host; three minutes is far past a slow boot and
    // far under the module timeout.
    private static readonly TimeSpan _StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly BrokerPortForwarder _forwarder;
    private readonly ContainerLogCapture _log = new();

    // Its tests stop and start the broker, which would cut every other fixture on a shared one.
    protected override bool ShareContainer => false;

    public FixedPortRabbitMqFixture()
    {
        _forwarder = new BrokerPortForwarder(() => Container, RabbitMqBuilder.RabbitMqPort);
    }

    public string ForwardedHost => _forwarder.Host;

    public int ForwardedPort => _forwarder.Port;

    /// <summary>Starts the broker, or fails with its log when it does not report startup in time.</summary>
    /// <exception cref="TimeoutException">The broker did not report startup within the bound.</exception>
    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        // The base start runs on the test's own token, so the bound stops the wait rather than the start: a start
        // abandoned here leaves a container this fixture never got, which the resource reaper removes with the run.
        try
        {
            await InitializeAsync().AsTask().WaitAsync(_StartupTimeout, cancellationToken);
        }
        catch (TimeoutException e)
        {
            throw _StartupTimedOut(e);
        }
    }

    /// <summary>Starts the stopped broker again, or fails with its log when it does not report startup in time.</summary>
    /// <exception cref="TimeoutException">The broker did not report startup within the bound.</exception>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(_StartupTimeout);

        try
        {
            await Container.StartAsync(bound.Token);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw _StartupTimedOut(e);
        }
    }

    protected override RabbitMqBuilder ConfigureContainer(RabbitMqBuilder builder)
    {
        return builder.WithOutputConsumer(_log);
    }

    private TimeoutException _StartupTimedOut(Exception inner)
    {
        return new TimeoutException(
            $"RabbitMQ did not report startup within {_StartupTimeout}. Its log so far:{Environment.NewLine}{_log}",
            inner
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            await base.DisposeAsyncCore();
        }
        finally
        {
            await _forwarder.DisposeAsync();
        }
    }
}

/// <summary>Keeps everything a container writes to stdout and stderr, for a failure message.</summary>
internal sealed class ContainerLogCapture : IOutputConsumer
{
    private readonly LogStream _stdout = new();
    private readonly LogStream _stderr = new();

    public bool Enabled => true;

    public Stream Stdout => _stdout;

    public Stream Stderr => _stderr;

    public override string ToString()
    {
        return $"stdout:{Environment.NewLine}{_stdout}{Environment.NewLine}stderr:{Environment.NewLine}{_stderr}";
    }

    public void Dispose() { }

    // Testcontainers writes from its log-follow task while a timed-out start reads, so every access takes the lock.
    private sealed class LogStream : Stream
    {
        private readonly Lock _lock = new();
        private readonly MemoryStream _buffer = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                _buffer.Write(buffer, offset, count);
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override string ToString()
        {
            lock (_lock)
            {
                return Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
