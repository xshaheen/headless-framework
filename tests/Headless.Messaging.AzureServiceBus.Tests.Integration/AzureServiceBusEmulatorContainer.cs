// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace Tests;

/// <summary>
/// The Azure Service Bus emulator and the SQL Server it keeps its entities in, on a network of their own. The emulator
/// creates only the entities <c>Emulator/Config.json</c> declares, and recreates them, empty, on every start.
/// </summary>
internal sealed class AzureServiceBusEmulatorContainer : IAsyncDisposable
{
    // Pinned by the multi-arch index digest, like every image in Headless.Testing.Testcontainers.TestImages, which this
    // project does not reference. 2.0.1 fixed idle session links being dropped while a handler ran.
    private const string _EmulatorImage =
        "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1@sha256:5a96d893b245031740f7d46e0fe5ff282d24b78c4b7d761dd57590f3f010a9b3";

    // The same pins as TestImages.MsSqlServer and TestImages.AzureSqlEdge. SQL Server ships no arm64 image, so an arm64
    // host uses Azure SQL Edge, as HeadlessSqlServerFixture does.
    private const string _SqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU19-ubuntu-22.04@sha256:147ee765ff1db3b86ce6ec05908e51fd0dab2feda5dd85b2721f28c77ca305eb";

    private const string _AzureSqlEdgeImage =
        "mcr.microsoft.com/azure-sql-edge:1.0.7@sha256:1dcc88d2d9e555d0addb0636648d0da033206978d7c5c4da044c904a0f06f58b";

    private const int _AmqpPort = 5672;
    private const int _ManagementPort = 5300;
    private const int _SqlPort = 1433;
    private const string _SqlAlias = "sql";

    // A throwaway password for a database that lives only as long as this container pair.
    private const string _SqlPassword = "Headless_Emulator#2026";

    private const string _ConfigPath = "/ServiceBus_Emulator/ConfigFiles/Config.json";

    private readonly INetwork _network;
    private readonly IContainer _sql;
    private readonly IContainer _emulator;

    private AzureServiceBusEmulatorContainer(INetwork network, IContainer sql, IContainer emulator)
    {
        _network = network;
        _sql = sql;
        _emulator = emulator;
    }

    /// <summary>
    /// Gets the connection string for the emulator's AMQP endpoint. It names no management port, so a client built from
    /// it cannot provision entities, which matches running the provider with <c>AutoProvision = false</c>.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Starts SQL Server and then the emulator. Pass <paramref name="amqpHostPort"/> to publish AMQP on a fixed host port,
    /// so a client keeps its address across <see cref="RestartAsync"/>; otherwise the port is random.
    /// </summary>
    /// <remarks>
    /// Both images start only with their license accepted. The caller must have the user's explicit opt-in before it
    /// calls this; the fixture checks it.
    /// </remarks>
    public static async Task<AzureServiceBusEmulatorContainer> StartAsync(
        int? amqpHostPort,
        CancellationToken cancellationToken
    )
    {
        var config = await File.ReadAllBytesAsync(
                Path.Combine(AppContext.BaseDirectory, "Emulator", "Config.json"),
                cancellationToken
            )
            .ConfigureAwait(false);
        var sqlImage =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? _AzureSqlEdgeImage : _SqlServerImage;

        var network = new NetworkBuilder().Build();
        var sql = new ContainerBuilder(sqlImage)
            .WithNetwork(network)
            .WithNetworkAliases(_SqlAlias)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", _SqlPassword)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(_SqlPort))
            .Build();
        var emulatorBuilder = new ContainerBuilder(_EmulatorImage)
            .WithNetwork(network)
            .WithResourceMapping(config, _ConfigPath)
            .WithPortBinding(_ManagementPort, assignRandomHostPort: true)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", _SqlAlias)
            .WithEnvironment("MSSQL_SA_PASSWORD", _SqlPassword)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    // Bounded, because an emulator whose internal queue fails to start never logs this line.
                    .UntilMessageIsLogged(
                        "Emulator Service is Successfully Up!",
                        wait => wait.WithTimeout(TimeSpan.FromMinutes(5))
                    )
                    .UntilHttpRequestIsSucceeded(request => request.ForPort(_ManagementPort).ForPath("/health"))
            );
        emulatorBuilder = amqpHostPort is { } hostPort
            ? emulatorBuilder.WithPortBinding(hostPort, _AmqpPort)
            : emulatorBuilder.WithPortBinding(_AmqpPort, assignRandomHostPort: true);
        var emulator = emulatorBuilder.Build();

        var started = new AzureServiceBusEmulatorContainer(network, sql, emulator);

        try
        {
            await network.CreateAsync(cancellationToken).ConfigureAwait(false);
            await sql.StartAsync(cancellationToken).ConfigureAwait(false);
            await emulator.StartAsync(cancellationToken).ConfigureAwait(false);
            started.ConnectionString = started._BuildConnectionString();
            return started;
        }
        catch
        {
            await started.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Stops and starts the emulator while SQL Server keeps running, the documented way to reset it. Messages are lost;
    /// the entities in <c>Config.json</c> come back.
    /// </summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _emulator.StopAsync(cancellationToken).ConfigureAwait(false);
        await _emulator.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _emulator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _sql.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await _network.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private string _BuildConnectionString()
    {
        var port = _emulator.GetMappedPublicPort(_AmqpPort).ToString(CultureInfo.InvariantCulture);

        // The emulator's documented development key; it guards nothing outside this container.
        return $"Endpoint=sb://{_emulator.Hostname}:{port};SharedAccessKeyName=RootManageSharedAccessKey;"
            + "SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
    }
}
