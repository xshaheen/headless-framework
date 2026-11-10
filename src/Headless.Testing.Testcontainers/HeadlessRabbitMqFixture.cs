// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Testcontainers.RabbitMq;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// RabbitMQ test fixture pinned to <see cref="TestImages.RabbitMq"/> that gives each fixture a virtual host of its own.
/// </summary>
/// <remarks>
/// <para>
/// Connect through <see cref="ConnectionString"/>, or through <see cref="HostName"/>, <see cref="Port"/>,
/// <see cref="UserName"/>, <see cref="Password"/>, and <see cref="VirtualHost"/>. The fixture creates the virtual host
/// on start and deletes it, with every exchange and queue in it, on dispose. Pass <c>-p</c> <see cref="VirtualHost"/>
/// to any <c>rabbitmqctl</c> command run through <see cref="Container"/>.
/// </para>
/// <para>
/// When <see cref="SharedTestContainers.IsEnabled"/> and <see cref="ShareContainer"/> are both true, every fixture
/// attaches to one long-lived RabbitMQ container instead of starting its own. A fixture that starts later deletes the
/// virtual hosts no connection is using, so a killed run leaves nothing behind for the next one.
/// </para>
/// <para>
/// Override <see cref="ConfigureContainer"/> only together with <see cref="ShareContainer"/> returning
/// <see langword="false"/>, as a fixture that stops or restarts its broker must.
/// </para>
/// </remarks>
[PublicAPI]
public class HeadlessRabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;
    private RabbitMqVirtualHostOwnership? _ownership;
    private bool _shared;

    /// <summary>
    /// Gets whether this fixture may attach to the shared RabbitMQ container when sharing is on. Override it to return
    /// <see langword="false"/> for a fixture that stops, pauses, or restarts its broker, or whose tests read
    /// broker-wide state another run would disturb.
    /// </summary>
    protected virtual bool ShareContainer => true;

    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.RabbitMq"/> unless overridden. Override it to run another
    /// version or a variant of the same server. The image is part of the shared container's name, so fixtures on
    /// different images share separate brokers and never attach to each other's.
    /// </summary>
    /// <remarks>The image must keep the official <c>rabbitmq</c> entrypoint and <c>rabbitmqctl</c>, which the fixture uses.</remarks>
    protected virtual string Image => TestImages.RabbitMq;

    /// <summary>Gets the started container, for its host, mapped port, or command execution.</summary>
    public RabbitMqContainer Container =>
        _container ?? throw new InvalidOperationException("The RabbitMQ fixture has not been initialized.");

    /// <summary>Gets the virtual host this fixture owns.</summary>
    public string VirtualHost => Ownership.VirtualHost;

    /// <summary>Gets the AMQP URI of this fixture's virtual host.</summary>
    public string ConnectionString
    {
        get
        {
#pragma warning disable RS0030 // The one sanctioned call: it supplies host, port, and credentials, and the virtual host is replaced.
            var uri = new UriBuilder(Container.GetConnectionString())
            {
                Path = "/" + Uri.EscapeDataString(VirtualHost),
            };
#pragma warning restore RS0030
            return uri.Uri.ToString();
        }
    }

    /// <summary>Gets the broker's host name.</summary>
    public string HostName => Container.Hostname;

    /// <summary>Gets the broker's host-mapped AMQP port.</summary>
    public int Port => Container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);

#pragma warning disable CA1822 // Instance members on purpose: tests read every connection setting from the fixture they were given.
    /// <summary>Gets the broker user name.</summary>
    public string UserName => RabbitMqBuilder.DefaultUsername;

    /// <summary>Gets the broker password.</summary>
    public string Password => RabbitMqBuilder.DefaultPassword;
#pragma warning restore CA1822

    private RabbitMqVirtualHostOwnership Ownership =>
        _ownership ?? throw new InvalidOperationException("The RabbitMQ fixture has not been initialized.");

    /// <summary>Adjusts a private container's builder, for a fixture whose <see cref="ShareContainer"/> is false.</summary>
    protected virtual RabbitMqBuilder ConfigureContainer(RabbitMqBuilder builder)
    {
        return builder;
    }

    /// <summary>Starts or attaches to the RabbitMQ container, then creates the fixture's virtual host.</summary>
    /// <exception cref="InvalidOperationException">
    /// A fixture that shares its container also overrides <see cref="ConfigureContainer"/>.
    /// </exception>
    protected virtual async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _shared = SharedTestContainers.IsEnabled && ShareContainer;

        if (ShareContainer && _OverridesConfigureContainer())
        {
            throw new InvalidOperationException(
                $"{GetType().Name} overrides ConfigureContainer, so it must also return false from ShareContainer: a shared broker keeps one configuration for every run."
            );
        }

        _container = await SharedTestContainers.StartAsync(_Build, cancellationToken).ConfigureAwait(false);
        await ContainerHostPort
            .EnsureReachableAsync(
                _container,
                RabbitMqBuilder.RabbitMqPort,
                allowRestart: !_shared,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        await _WaitUntilBrokerAnswersAsync(_container, cancellationToken).ConfigureAwait(false);

        if (_shared)
        {
            // A client that leaves out the virtual host lands in the default one, where every run would meet every
            // other run's exchanges. Refuse it there, so a connection that forgot VirtualHost fails at once instead of
            // sharing state.
            await _container
                .ExecAsync(["rabbitmqctl", "clear_permissions", "-p", "/", UserName], cancellationToken)
                .ConfigureAwait(false);
        }

        _ownership = await RabbitMqVirtualHostOwnership
            .CreateAsync(_container, (ushort)Port, UserName, Password, cancellationToken)
            .ConfigureAwait(false);
    }

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return InitializeAsync();
    }

    /// <summary>Disposes the fixture through <see cref="DisposeAsyncCore"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Deletes the fixture's virtual host, then removes its container unless the container is shared.</summary>
    /// <remarks>A shared container is left running for the next fixture; disposing it would stop it under every other run.</remarks>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_ownership is not null)
        {
            await _ownership.DisposeAsync().ConfigureAwait(false);
        }

        if (_container is not null && !_shared)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    // The log-based wait can pass on a restarted broker before the rabbit app runs, and every rabbitmqctl command
    // fails until it does. `rabbitmqctl await_startup` returns at once with an error while the node itself is still
    // booting, so poll a cheap command instead.
    private static async Task _WaitUntilBrokerAnswersAsync(
        RabbitMqContainer container,
        CancellationToken cancellationToken
    )
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);

        while (true)
        {
            var result = await container
                .ExecAsync(["rabbitmqctl", "list_vhosts", "-q"], cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new InvalidOperationException($"The RabbitMQ broker did not start answering: {result.Stderr}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
        }
    }

    private RabbitMqContainer _Build()
    {
        var image = Image;
        var builder = new RabbitMqBuilder(image);

        builder = _shared
            ? SharedTestContainers.AsShared<RabbitMqBuilder, RabbitMqContainer>(
                builder,
                SharedTestContainers.ContainerName("rabbitmq", image, RabbitMqBuilder.DefaultUsername)
            )
            : ConfigureContainer(builder);

        return builder.Build();
    }

    private bool _OverridesConfigureContainer()
    {
        var method = GetType()
            .GetMethod(
                nameof(ConfigureContainer),
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(RabbitMqBuilder)]
            );

        return method is not null && method.DeclaringType != typeof(HeadlessRabbitMqFixture);
    }
}
